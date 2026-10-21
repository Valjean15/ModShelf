using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Fans job snapshots out to every watcher, each through its own channel.
/// </summary>
internal sealed class DownloadWatchers
{
    private readonly Lock _gate = new();
    private readonly List<ChannelWriter<DownloadJobSnapshot>> _writers = [];
    private bool _completed;

    /// <summary>
    /// Subscribes right away, so no snapshot published after this call is missed, even before the caller
    /// starts enumerating. Cancelling <paramref name="cancellationToken"/> ends the stream.
    /// </summary>
    public IAsyncEnumerable<DownloadJobSnapshot> Watch(CancellationToken cancellationToken)
    {
        Channel<DownloadJobSnapshot> channel = Channel.CreateUnbounded<DownloadJobSnapshot>(
            new UnboundedChannelOptions { SingleReader = true });

        lock (_gate)
        {
            if (_completed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _writers.Add(channel.Writer);
            }
        }

        // Cancelling completes the channel, so the stream ends normally instead of throwing.
        CancellationTokenRegistration registration = cancellationToken.Register(() => Remove(channel.Writer));
        return ReadAllAsync(channel, registration, CancellationToken.None);
    }

    public void Publish(DownloadJobSnapshot snapshot)
    {
        lock (_gate)
        {
            foreach (ChannelWriter<DownloadJobSnapshot> writer in _writers)
            {
                writer.TryWrite(snapshot);
            }
        }
    }

    /// <summary>
    /// Ends every stream, current and future. Used when the scheduler stops.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            foreach (ChannelWriter<DownloadJobSnapshot> writer in _writers)
            {
                writer.TryComplete();
            }

            _writers.Clear();
        }
    }

    private async IAsyncEnumerable<DownloadJobSnapshot> ReadAllAsync(
        Channel<DownloadJobSnapshot> channel,
        CancellationTokenRegistration registration,
        [EnumeratorCancellation] CancellationToken enumeratorCancellation = default)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(enumeratorCancellation).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out DownloadJobSnapshot? snapshot))
                {
                    yield return snapshot;
                }
            }
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            Remove(channel.Writer);
        }
    }

    private void Remove(ChannelWriter<DownloadJobSnapshot> writer)
    {
        lock (_gate)
        {
            _writers.Remove(writer);
        }

        writer.TryComplete();
    }
}
