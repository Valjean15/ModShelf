namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The watchers of a fake's changes, such as the download queue's. Publishing a change returns only once every watcher
/// has handled it, which it has once it asks for the next one, so a test sees what a screen made of it straight after.
/// </summary>
/// <remarks>
/// A stream that only signalled from inside <c>MoveNextAsync</c> would signal before the awaiter had registered its
/// continuation, so a change published right after could be handled on another thread after the publisher returned.
/// </remarks>
internal sealed class ControlledWatchers<T>
    where T : class
{
    private readonly Lock _gate = new();
    private readonly List<Stream> _streams = [];
    private readonly TaskCompletionSource _waitedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Done once a watcher has read everything published and waits for more.
    /// </summary>
    public Task WaitedOn => _waitedOn.Task;

    /// <summary>
    /// A new watcher's stream, watching from now on. Cancelling ends it.
    /// </summary>
    public IAsyncEnumerable<T> Watch(CancellationToken cancellationToken)
    {
        Stream stream = new(_waitedOn, cancellationToken);
        lock (_gate)
        {
            _streams.Add(stream);
        }

        return stream;
    }

    /// <summary>
    /// Tells every watcher of <paramref name="item"/>, and returns once each has handled it.
    /// </summary>
    public void Publish(T item)
    {
        Stream[] streams;
        lock (_gate)
        {
            streams = [.. _streams];
        }

        foreach (Stream stream in streams)
        {
            stream.Publish(item);
        }
    }

    /// <summary>
    /// One watcher's stream. Publishing waits until the watcher has handled the change, which it has once it asks for the
    /// next one; cancelling ends the stream.
    /// </summary>
    private sealed class Stream : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);

        private readonly Lock _gate = new();
        private readonly Queue<T> _queued = new();
        private readonly TaskCompletionSource _waitedOn;
        private readonly CancellationTokenRegistration _cancelling;
        private TaskCompletionSource<bool>? _waiting;
        private bool _ended;
        private int _published;
        private int _asked;

        public Stream(TaskCompletionSource waitedOn, CancellationToken cancellationToken)
        {
            _waitedOn = waitedOn;
            _cancelling = cancellationToken.Register(End);
        }

        public T Current { get; private set; } = null!;

        private int Asked
        {
            get
            {
                lock (_gate)
                {
                    return _asked;
                }
            }
        }

        private bool Ended
        {
            get
            {
                lock (_gate)
                {
                    return _ended;
                }
            }
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            lock (_gate)
            {
                _asked++;
                if (_queued.TryDequeue(out T? next))
                {
                    Current = next;
                    return ValueTask.FromResult(true);
                }

                if (_ended)
                {
                    return ValueTask.FromResult(false);
                }

                _waiting = new TaskCompletionSource<bool>();
            }

            _waitedOn.TrySetResult();
            return new ValueTask<bool>(_waiting.Task);
        }

        public void Publish(T item)
        {
            TaskCompletionSource<bool>? waiting;
            int handledOnceAsked;
            lock (_gate)
            {
                // The watcher asked once for each change before this one, and once for this one; it asks again when done.
                handledOnceAsked = ++_published + 1;
                waiting = _waiting;
                _waiting = null;
                if (waiting is null)
                {
                    _queued.Enqueue(item);
                }
                else
                {
                    Current = item;
                }
            }

            waiting?.SetResult(true);
            if (!SpinWait.SpinUntil(() => Asked >= handledOnceAsked || Ended, LongestWait))
            {
                throw new TimeoutException("The watcher didn't handle the change.");
            }
        }

        public async ValueTask DisposeAsync() => await _cancelling.DisposeAsync();

        private void End()
        {
            TaskCompletionSource<bool>? waiting;
            lock (_gate)
            {
                _ended = true;
                waiting = _waiting;
                _waiting = null;
            }

            waiting?.SetResult(false);
        }
    }
}
