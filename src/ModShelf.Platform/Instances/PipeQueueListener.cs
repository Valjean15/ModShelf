using System.IO.Pipes;
using System.Text;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;

namespace ModShelf.Platform.Instances;

/// <summary>
/// In the desktop app, which runs the library's download queue: answers the queue requests other processes send, such
/// as the CLI's MCP server, over the queue's pipe (<see cref="PipeQueueOwner"/>). Each request is carried out on its
/// own, so a link that takes long to open keeps no other request waiting.
/// </summary>
/// <remarks>
/// The pipe takes only the same user's connections, as the single instance's does. A request the app can't read, such
/// as a newer app's, gets a refusal that says so. Requests carry links, which are never logged.
/// </remarks>
internal sealed partial class PipeQueueListener : BackgroundService
{
    /// <summary>
    /// What tells the queue's pipe from the single instance's.
    /// </summary>
    internal const string Suffix = "-queue";

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _pipeName;
    private readonly IQueueRequests _requests;
    private readonly TimeSpan _patience;
    private readonly ILogger _logger;

    /// <param name="folder">The folder of the library, an absolute path.</param>
    /// <param name="patience">How long a request's message, and then its reply, may take on the pipe.</param>
    public PipeQueueListener(string folder, IQueueRequests requests, TimeSpan patience, ILogger logger)
        : this(folder, requests, patience, logger, OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") : null)
    {
    }

    /// <param name="runtimeFolder">The user's own folder for the pipe's socket, if any.</param>
    internal PipeQueueListener(string folder, IQueueRequests requests, TimeSpan patience, ILogger logger, string? runtimeFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"'{folder}' is not an absolute path.", nameof(folder));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(patience, TimeSpan.Zero);

        _pipeName = PipeSingleInstance.PipeNameFor(folder, runtimeFolder, Suffix);
        _requests = requests;
        _patience = patience;
        _logger = logger;
    }

    /// <summary>
    /// The pipe's name, or its socket's full path.
    /// </summary>
    internal string PipeName => _pipeName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NamedPipeServerStream? waiting = null;
        List<Task> answering = [];
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    waiting ??= NewPipe();
                    await waiting.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Such as a pipe that can't be made, which keeps failing, so the loop waits a little before it tries
                    // again.
                    LogCantListen(_logger, exception);
                    waiting?.Dispose();
                    waiting = null;
                    if (!await PauseAsync(stoppingToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                // The request is answered on its own, while the loop makes the next one a pipe: on Linux and macOS, the
                // pipe is a socket that closes with its last server.
                answering.RemoveAll(task => task.IsCompleted);
                answering.Add(AnswerAsync(waiting, stoppingToken));
                waiting = null;
            }
        }
        finally
        {
            waiting?.Dispose();

            // Stopping cancels the requests being carried out; each ends at once, its pipe closed.
            await Task.WhenAll(answering).ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream NewPipe()
        => new(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <returns>False when stopped meanwhile.</returns>
    private async Task<bool> PauseAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_patience, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a request, carries it out, and replies; then closes the pipe. Never throws.
    /// </summary>
    private async Task AnswerAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        // Off the accepting loop at once, which then makes the next pipe.
        await Task.Yield();
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using StreamReader reader = new(pipe, Utf8, leaveOpen: true);
                using StreamWriter writer = new(pipe, Utf8, leaveOpen: true) { AutoFlush = true };

                string? line;
                using (CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    reading.CancelAfter(_patience);
                    try
                    {
                        line = await PipeLines.ReadLineAsync(reader, QueueMessages.MaxLineLength, reading.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is InvalidDataException
                        || (exception is OperationCanceledException && !stoppingToken.IsCancellationRequested))
                    {
                        LogUnreadable(_logger, exception);
                        return;
                    }
                }

                if (line is null)
                {
                    LogUnreadable(_logger, null);
                    return;
                }

                QueueReply reply;
                try
                {
                    reply = await _requests.HandleAsync(QueueMessages.ReadRequest(line), stoppingToken).ConfigureAwait(false);
                }
                catch (InvalidDataException exception)
                {
                    LogUnreadable(_logger, exception);
                    reply = new QueueReply(QueueOutcome.Refused, "The ModShelf app that's running doesn't know this request. Update it, then ask again.");
                }

                using CancellationTokenSource replying = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                replying.CancelAfter(_patience);
                await writer.WriteLineAsync(QueueMessages.Write(reply).AsMemory(), replying.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The app is closing: the asking side sees the pipe close without a reply.
            }
            catch (Exception exception)
            {
                // Such as an asking side that hung up before its reply.
                LogCantReply(_logger, exception);
            }
        }
    }
}
