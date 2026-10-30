using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Desktop;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Domain.Common;

namespace ModShelf.Platform.Instances;

/// <summary>
/// One desktop app per folder: the one running holds <c>desktop.lock</c> in the folder with no sharing, and listens on a
/// named pipe whose name comes from the lock's path. A second launch finds the lock held, sends its arguments down the
/// pipe as a line of JSON, and waits for the answer.
/// </summary>
/// <remarks>
/// <para>
/// The system lets go of the lock when the process ends, however it ends, as with the download queue's lock (ADR 0027).
/// The pipe takes only the same user's connections. On Linux and macOS, .NET makes it a socket: on Linux, in the user's
/// own runtime folder (<c>$XDG_RUNTIME_DIR</c>) where there's one, since another user could take its name first in the
/// shared temporary folder; otherwise, and on macOS, whose temporary folder is the user's own, in the temporary folder.
/// </para>
/// <para>The arguments are never logged: from Phase 6, they can hold <c>nxm://</c> links and their keys.</para>
/// </remarks>
internal sealed partial class PipeSingleInstance : ISingleInstance, IDisposable
{
    private const string LockFileName = "desktop.lock";
    private const string Answer = "ok";

    // A launch's arguments are a few links at most.
    private const int MaxMessageLength = 64 * 1024;

    // The longest socket path Linux and macOS take is 107 and 103 bytes; a longer one goes in the temporary folder.
    private const int MaxSocketPathLength = 100;

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _folder;
    private readonly string _pipeName;
    private readonly TimeSpan _patience;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ManualResetEventSlim _stopped = new(true);
    private readonly Lock _gate = new();
    private FileStream? _lock;
    private bool _disposed;

    /// <param name="folder">The folder of the library the desktop app runs for, an absolute path.</param>
    /// <param name="patience">How long a second launch waits for the running app to answer.</param>
    public PipeSingleInstance(string folder, TimeSpan patience, ILogger logger)
        : this(folder, patience, logger, OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") : null)
    {
    }

    /// <param name="folder">The folder of the library the desktop app runs for, an absolute path.</param>
    /// <param name="patience">How long a second launch waits for the running app to answer.</param>
    /// <param name="runtimeFolder">The user's own folder for the pipe's socket, if any.</param>
    internal PipeSingleInstance(string folder, TimeSpan patience, ILogger logger, string? runtimeFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"'{folder}' is not an absolute path.", nameof(folder));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(patience, TimeSpan.Zero);

        _folder = Path.GetFullPath(folder);
        _pipeName = PipeNameFor(_folder, runtimeFolder);
        _patience = patience;
        _logger = logger;
    }

    public event EventHandler<Launch>? LaunchedAgain;

    /// <summary>
    /// The pipe's name, or its socket's full path.
    /// </summary>
    internal string PipeName => _pipeName;

    /// <exception cref="UnauthorizedAccessException">The lock file can't be created, as in a folder this user can't write to.</exception>
    public bool TryClaim(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lock is not null)
            {
                return true;
            }

            Directory.CreateDirectory(_folder);
            try
            {
                _lock = new FileStream(Path.Combine(_folder, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Another desktop app holds it.
                _lock = null;
            }

            if (_lock is not null)
            {
                _stopped.Reset();
                _ = Task.Run(() => ListenAsync(_stopping.Token));
                return true;
            }
        }

        HandOver(arguments);
        return false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping.Cancel();
        }

        // Listening ends at once once cancelled; waiting for it keeps the pipe from outliving the lock, so an app started
        // right after can listen on it.
        _stopped.Wait(_patience);
        _lock?.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }

    /// <summary>
    /// The pipe's name: ModShelf's, and a hash of the folder's path, in upper case where paths ignore case, so each
    /// library's app has its own and the name holds no character a pipe's can't. With a runtime folder that's there, and
    /// leaves room, it's a full path in that folder, which .NET takes as the socket's path.
    /// </summary>
    /// <param name="runtimeFolder">The user's own folder for the socket, as on Linux; null for none.</param>
    internal static string PipeNameFor(string folder, string? runtimeFolder = null)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (FilePaths.Comparison == StringComparison.OrdinalIgnoreCase)
        {
            path = path.ToUpperInvariant();
        }

        string name = "ModShelf-" + Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(path)))[..24];
        if (string.IsNullOrEmpty(runtimeFolder) || !Path.IsPathFullyQualified(runtimeFolder) || !Directory.Exists(runtimeFolder))
        {
            return name;
        }

        string socket = Path.Combine(runtimeFolder, name);
        return Utf8.GetByteCount(socket) <= MaxSocketPathLength ? socket : name;
    }

    /// <summary>
    /// A line of at most <see cref="MaxMessageLength"/> characters.
    /// </summary>
    /// <exception cref="InvalidDataException">The line is longer.</exception>
    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        StringBuilder line = new();
        char[] next = new char[1];
        while (await reader.ReadAsync(next, cancellationToken).ConfigureAwait(false) == 1)
        {
            if (next[0] == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (line.Length == MaxMessageLength)
            {
                throw new InvalidDataException($"A launch's message is longer than {MaxMessageLength} characters.");
            }

            line.Append(next[0]);
        }

        return line.Length == 0 ? null : line.ToString();
    }

    /// <summary>
    /// Sends <paramref name="arguments"/> to the desktop app that's running, and waits for it to answer.
    /// </summary>
    /// <exception cref="TimeoutException">It didn't answer in time.</exception>
    private void HandOver(IReadOnlyList<string> arguments)
    {
        try
        {
            // Asynchronous, so that closing the pipe ends a read or a write waiting on it: on Windows, a synchronous one keeps
            // the pipe open until it's done, which a running app that never answers never lets it be.
            using NamedPipeClientStream pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(_patience);

            // Pipes have no timeouts of their own, so a timer closes the pipe under a reply that doesn't come.
            using Timer giveUp = new(_ => pipe.Dispose(), null, _patience, Timeout.InfiniteTimeSpan);
            using StreamWriter writer = new(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
            using StreamReader reader = new(pipe, Utf8, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(new Message([.. arguments])));
            if (reader.ReadLine() != Answer)
            {
                throw new TimeoutException("ModShelf is running already, but didn't answer.");
            }
        }
        // On Windows, a read or a write the timer ended throws OperationCanceledException.
        catch (Exception exception) when (exception is TimeoutException or IOException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
        {
            throw new TimeoutException("ModShelf is running already, but didn't answer.", exception);
        }
    }

    /// <summary>
    /// Answers each launch after this one, one at a time, until stopped.
    /// </summary>
    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        try
        {
            await AnswerEachAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            _stopped.Set();
        }
    }

    private async Task AnswerEachAsync(CancellationToken stoppingToken)
    {
        NamedPipeServerStream? waiting = null;
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
                    // Such as a pipe that can't be made, which keeps failing, so the loop waits a little before it
                    // tries again.
                    LogCantListen(_logger, exception);
                    waiting?.Dispose();
                    waiting = null;
                    if (!await PauseAsync(stoppingToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                // The next launch gets a pipe of its own while this one is answered. On Linux and macOS, the pipe is a
                // socket that closes with its last server, so a launch coming in between would be cut off.
                NamedPipeServerStream connected = waiting;
                waiting = null;
                try
                {
                    waiting = NewPipe();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Made again at the loop's start.
                    LogCantListen(_logger, exception);
                }

                await using (connected.ConfigureAwait(false))
                {
                    try
                    {
                        await AnswerAsync(connected, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // Such as a launch that hung up before its answer.
                        LogUnreadable(_logger, exception);
                    }
                }
            }
        }
        finally
        {
            waiting?.Dispose();
        }
    }

    private NamedPipeServerStream NewPipe()
        => new(_pipeName, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

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
    /// Reads a launch's message, tells whoever listens, and answers.
    /// </summary>
    private async Task AnswerAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        reading.CancelAfter(_patience);
        using StreamReader reader = new(pipe, Utf8, leaveOpen: true);
        using StreamWriter writer = new(pipe, Utf8, leaveOpen: true) { AutoFlush = true };

        Message? message;
        try
        {
            string? line = await ReadLineAsync(reader, reading.Token).ConfigureAwait(false);
            message = line is null ? null : JsonSerializer.Deserialize<Message>(line);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException
            || (exception is OperationCanceledException && !stoppingToken.IsCancellationRequested))
        {
            LogUnreadable(_logger, exception);
            return;
        }

        if (message?.Arguments is not { } arguments)
        {
            LogUnreadable(_logger, null);
            return;
        }

        LogLaunchedAgain(_logger);
        try
        {
            LaunchedAgain?.Invoke(this, new Launch(arguments));
        }
        catch (Exception exception)
        {
            // Whatever the app does with it, the launch gets its answer, and the next one is heard.
            LogHandlerFailed(_logger, exception);
        }

        await writer.WriteLineAsync(Answer.AsMemory(), reading.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// What a launch sends: its arguments.
    /// </summary>
    private sealed record Message(string[]? Arguments);
}
