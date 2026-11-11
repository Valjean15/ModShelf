using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A browser's downloads folder whose files the test makes appear, recording which folders are watched, and when a
/// watch ends.
/// </summary>
internal sealed class ScriptedBrowserDownloads : IBrowserDownloads
{
    private readonly Channel<string> _files = Channel.CreateUnbounded<string>();
    private int _watching;

    /// <summary>
    /// Each folder watched, in turn.
    /// </summary>
    public ConcurrentQueue<string> Watched { get; } = new();

    /// <summary>
    /// How many watches are going on now.
    /// </summary>
    public int Watching => Volatile.Read(ref _watching);

    /// <summary>
    /// What watching a folder throws, if anything, such as for one that isn't there.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// A file appears, complete, in the folder watched.
    /// </summary>
    public void Appear(string path) => _files.Writer.TryWrite(path);

    public async IAsyncEnumerable<string> WatchAsync(string folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Watched.Enqueue(folder);
        if (Failure is { } failure)
        {
            throw failure;
        }

        Interlocked.Increment(ref _watching);
        try
        {
            while (await _files.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_files.Reader.TryRead(out string? path))
                {
                    yield return path;
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _watching);
        }
    }
}
