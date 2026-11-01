using System.IO.Pipes;
using System.Text;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Platform.Instances;

/// <summary>
/// The desktop app that runs this library's download queue, asked over the queue's pipe, which
/// <see cref="PipeQueueListener"/> answers: a line of JSON there and back.
/// </summary>
/// <remarks>
/// The pipe is the library's, as the single instance's is, with a name of its own, and takes only the same user's
/// connections. A pipe another user made isn't asked. Requests carry links, which are never logged.
/// </remarks>
internal sealed partial class PipeQueueOwner : IQueueOwner
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _pipeName;
    private readonly TimeSpan _patience;
    private readonly TimeSpan _replyTimeout;
    private readonly ILogger _logger;

    /// <param name="folder">The folder of the library, an absolute path.</param>
    /// <param name="patience">How long to wait for the desktop app to take the request: no longer, when none runs.</param>
    /// <param name="replyTimeout">How long to wait for its reply, once it has the request.</param>
    public PipeQueueOwner(string folder, TimeSpan patience, TimeSpan replyTimeout, ILogger logger)
        : this(folder, patience, replyTimeout, logger, OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") : null)
    {
    }

    /// <param name="runtimeFolder">The user's own folder for the pipe's socket, if any.</param>
    internal PipeQueueOwner(string folder, TimeSpan patience, TimeSpan replyTimeout, ILogger logger, string? runtimeFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"'{folder}' is not an absolute path.", nameof(folder));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(patience, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(replyTimeout, TimeSpan.Zero);

        _pipeName = PipeSingleInstance.PipeNameFor(folder, runtimeFolder, PipeQueueListener.Suffix);
        _patience = patience;
        _replyTimeout = replyTimeout;
        _logger = logger;
    }

    /// <exception cref="TimeoutException">The desktop app took the request, but didn't reply in time, or hung up.</exception>
    /// <exception cref="InvalidDataException">Its reply couldn't be read, as from a newer app.</exception>
    public async Task<QueueReply?> SendAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string message = QueueMessages.Write(request);

        NamedPipeClientStream pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                await pipe.ConnectAsync(_patience, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // No desktop app runs, or one from before the queue's pipe (ADR 0040).
                LogNoDesktopApp(_logger);
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                LogNotThisUsers(_logger, exception);
                return null;
            }

            using CancellationTokenSource replying = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            replying.CancelAfter(_replyTimeout);
            try
            {
                // Closing the pipe ends a read or a write cancelling doesn't, on any system.
                using CancellationTokenRegistration closing = replying.Token.Register(pipe.Dispose);
                using StreamWriter writer = new(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
                using StreamReader reader = new(pipe, Utf8, leaveOpen: true);
                await writer.WriteLineAsync(message.AsMemory(), replying.Token).ConfigureAwait(false);
                string? reply = await PipeLines.ReadLineAsync(reader, QueueMessages.MaxLineLength, replying.Token).ConfigureAwait(false)
                    ?? throw new IOException("The desktop app hung up without replying.");
                return QueueMessages.ReadReply(reply);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogNoReply(_logger, exception);
                throw new TimeoutException("ModShelf's desktop app took the request, but didn't reply in time.", exception);
            }
        }
    }
}
