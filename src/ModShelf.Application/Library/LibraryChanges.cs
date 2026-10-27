using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ModShelf.Application.Library;

/// <summary>
/// Fans each change to the library out to every watcher, each through its own channel, as the download queue's watchers
/// do. Whatever changes the library, through <see cref="Ports.ILibraryRepository.UpdateAsync"/>, publishes what it did.
/// </summary>
internal sealed class LibraryChanges
{
    private readonly Lock _gate = new();
    private readonly List<ChannelWriter<LibraryChange>> _writers = [];

    /// <summary>
    /// Subscribes right away, so no change published after this call is missed, even before the caller starts
    /// enumerating. Cancelling <paramref name="cancellationToken"/> ends the stream.
    /// </summary>
    public IAsyncEnumerable<LibraryChange> Watch(CancellationToken cancellationToken)
    {
        Channel<LibraryChange> channel = Channel.CreateUnbounded<LibraryChange>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            _writers.Add(channel.Writer);
        }

        // Cancelling completes the channel, so the stream ends normally instead of throwing.
        CancellationTokenRegistration registration = cancellationToken.Register(() => Remove(channel.Writer));
        return ReadAllAsync(channel, registration, CancellationToken.None);
    }

    /// <summary>
    /// Tells every watcher what <paramref name="change"/> did, unless it left the mod as it was read.
    /// </summary>
    public void Publish(LibraryChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (ReferenceEquals(change.Before, change.After))
        {
            return;
        }

        lock (_gate)
        {
            foreach (ChannelWriter<LibraryChange> writer in _writers)
            {
                writer.TryWrite(change);
            }
        }
    }

    private async IAsyncEnumerable<LibraryChange> ReadAllAsync(
        Channel<LibraryChange> channel,
        CancellationTokenRegistration registration,
        [EnumeratorCancellation] CancellationToken enumeratorCancellation = default)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(enumeratorCancellation).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out LibraryChange? change))
                {
                    yield return change;
                }
            }
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            Remove(channel.Writer);
        }
    }

    private void Remove(ChannelWriter<LibraryChange> writer)
    {
        lock (_gate)
        {
            _writers.Remove(writer);
        }

        writer.TryComplete();
    }
}
