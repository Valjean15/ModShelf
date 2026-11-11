using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Platform.Downloads;

/// <summary>
/// Watches the folder a browser saves downloads to for the files it finishes there (ADR 0091). A browser writes a file
/// under a name of its own, such as Chrome's and Edge's <c>.crdownload</c> or Firefox's <c>.part</c>, then renames it;
/// Firefox also leaves an empty file under the final name meanwhile. So a file counts once it has a name of its own,
/// isn't empty, has no such file beside it, keeps its size for a moment, and can be read.
/// </summary>
internal sealed partial class BrowserFolderWatcher : IBrowserDownloads
{
    /// <summary>
    /// The endings browsers give a file while they download it.
    /// </summary>
    private static readonly string[] Unfinished = [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload"];

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BrowserFolderWatcher> _logger;
    private readonly TimeSpan _settle;

    /// <param name="settle">How long a file keeps its size before it counts; two seconds unless given.</param>
    public BrowserFolderWatcher(TimeProvider timeProvider, ILogger<BrowserFolderWatcher> logger, TimeSpan? settle = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _timeProvider = timeProvider;
        _logger = logger;
        _settle = settle ?? TimeSpan.FromSeconds(2);
    }

    public async IAsyncEnumerable<string> WatchAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The browser's downloads folder isn't there.");
        }

        Channel<string> named = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        using FileSystemWatcher watcher = new(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        watcher.Created += (_, change) => named.Writer.TryWrite(change.FullPath);
        watcher.Changed += (_, change) => named.Writer.TryWrite(change.FullPath);
        watcher.Renamed += (_, change) => named.Writer.TryWrite(change.FullPath);
        watcher.Error += (_, error) => LogWatchFailed(_logger, error.GetException());
        watcher.EnableRaisingEvents = true;
        LogWatching(_logger);

        Dictionary<string, long> pending = new(StringComparer.Ordinal);
        HashSet<string> given = new(StringComparer.Ordinal);
        while (!cancellationToken.IsCancellationRequested)
        {
            Task<bool> more = named.Reader.WaitToReadAsync(cancellationToken).AsTask();
            Task tick = Task.Delay(_settle, _timeProvider, cancellationToken);
            await Task.WhenAny(more, tick).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            while (named.Reader.TryRead(out string? path))
            {
                if (!given.Contains(path) && !IsUnfinishedName(path))
                {
                    // A change starts the wait again.
                    pending[path] = -1;
                }
            }

            if (!tick.IsCompleted)
            {
                continue;
            }

            foreach (string path in pending.Keys.ToList())
            {
                long? size = Size(path);
                if (size is null)
                {
                    pending.Remove(path);
                }
                else if (size.Value > 0 && size.Value == pending[path] && !HasUnfinishedBeside(path) && CanRead(path))
                {
                    pending.Remove(path);
                    given.Add(path);
                    yield return path;
                }
                else
                {
                    pending[path] = size.Value;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Whether the name is one a browser gives a file while it downloads it.
    /// </summary>
    internal static bool IsUnfinishedName(string path)
        => Unfinished.Any(ending => path.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

    private static bool HasUnfinishedBeside(string path)
        => Unfinished.Any(ending => File.Exists(path + ending));

    private static long? Size(string path)
    {
        try
        {
            FileInfo file = new(path);
            return file.Exists ? file.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the file can be read while others read it too: a browser still writing it on Windows holds it alone.
    /// </summary>
    private static bool CanRead(string path)
    {
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
