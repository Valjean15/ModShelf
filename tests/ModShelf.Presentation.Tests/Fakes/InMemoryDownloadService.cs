using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A download queue in memory. Controlling a job changes it at once, and the change returns only once every watcher has
/// handled it, so a test sees what the screen made of it straight after.
/// </summary>
internal sealed class InMemoryDownloadService : IDownloadService
{
    private readonly Lock _gate = new();
    private readonly List<DownloadJobSnapshot> _jobs = [];
    private readonly List<Watch> _watches = [];
    private readonly TaskCompletionSource _waitedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// What was asked of the queue, in order, such as "Pause" with the job's ID.
    /// </summary>
    public List<(string Action, DownloadJobId Id)> Controls { get; } = [];

    public List<(ModFileRef File, Uri Link)> HandedOff { get; } = [];

    /// <summary>
    /// What pausing, resuming, retrying, cancelling and clearing throw, such as an
    /// <see cref="InvalidOperationException"/> for a job that changed meanwhile; null for none.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// What happens just after the queue is listed, such as a job added meanwhile, which the list doesn't have.
    /// </summary>
    public Action? AfterListing { get; set; }

    /// <summary>
    /// Done once a watcher has read everything published and waits for more.
    /// </summary>
    public Task WaitedOn => _waitedOn.Task;

    public IReadOnlyList<DownloadJobSnapshot> Jobs
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs];
            }
        }
    }

    /// <summary>
    /// Adds a job that was in the queue before anything watched it.
    /// </summary>
    public void Add(params DownloadJobSnapshot[] jobs)
    {
        lock (_gate)
        {
            _jobs.AddRange(jobs);
        }
    }

    /// <summary>
    /// Adds or changes a job, as the queue does when it works on one, and tells every watcher.
    /// </summary>
    public void Publish(DownloadJobSnapshot job)
    {
        Watch[] watches;
        lock (_gate)
        {
            int index = _jobs.FindIndex(listed => listed.Id == job.Id);
            if (index < 0)
            {
                _jobs.Add(job);
            }
            else
            {
                _jobs[index] = job;
            }

            watches = [.. _watches];
        }

        foreach (Watch watch in watches)
        {
            watch.Publish(job);
        }
    }

    public Task<DownloadJobSnapshot> EnqueueAsync(ModFileRef file, string targetDirectory, string fileName, CancellationToken cancellationToken)
    {
        DownloadJobSnapshot job = TestData.Job(fileName, folder: targetDirectory) with { File = file };
        Publish(job);
        return Task.FromResult(job);
    }

    public Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken)
    {
        HandedOff.Add((file, handoff));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DownloadJobSnapshot> jobs = Jobs;
        AfterListing?.Invoke();
        return Task.FromResult(jobs);
    }

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync("Pause", id, job => job with { Status = DownloadJobStatus.Paused });

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync("Resume", id, job => job with { Status = DownloadJobStatus.Queued });

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync("Retry", id, job => job with { Status = DownloadJobStatus.Queued, FailureReason = null });

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken)
        => ControlAsync("Cancel", id, job => job with { Status = DownloadJobStatus.Cancelled });

    public Task<int> ClearFinishedAsync(CancellationToken cancellationToken)
    {
        if (Refusal is not null)
        {
            throw Refusal;
        }

        lock (_gate)
        {
            return Task.FromResult(_jobs.RemoveAll(job => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled));
        }
    }

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken)
    {
        Watch watch = new(_waitedOn, cancellationToken);
        lock (_gate)
        {
            _watches.Add(watch);
        }

        return watch;
    }

    private Task ControlAsync(string action, DownloadJobId id, Func<DownloadJobSnapshot, DownloadJobSnapshot> change)
    {
        Controls.Add((action, id));
        if (Refusal is not null)
        {
            throw Refusal;
        }

        DownloadJobSnapshot job = Jobs.SingleOrDefault(listed => listed.Id == id) ?? throw new KeyNotFoundException($"There is no download job {id}.");
        Publish(change(job));
        return Task.CompletedTask;
    }

    /// <summary>
    /// One watcher's stream. Publishing waits until the watcher has handled the change, which it has once it asks for the
    /// next one; cancelling ends the stream.
    /// </summary>
    private sealed class Watch : IAsyncEnumerable<DownloadJobSnapshot>, IAsyncEnumerator<DownloadJobSnapshot>
    {
        private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);

        private readonly Lock _gate = new();
        private readonly Queue<DownloadJobSnapshot> _queued = new();
        private readonly TaskCompletionSource _waitedOn;
        private readonly CancellationTokenRegistration _cancelling;
        private TaskCompletionSource<bool>? _waiting;
        private bool _ended;
        private int _published;
        private int _asked;

        public Watch(TaskCompletionSource waitedOn, CancellationToken cancellationToken)
        {
            _waitedOn = waitedOn;
            _cancelling = cancellationToken.Register(End);
        }

        public DownloadJobSnapshot Current { get; private set; } = null!;

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

        public IAsyncEnumerator<DownloadJobSnapshot> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            lock (_gate)
            {
                _asked++;
                if (_queued.TryDequeue(out DownloadJobSnapshot? next))
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

        public void Publish(DownloadJobSnapshot job)
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
                    _queued.Enqueue(job);
                }
                else
                {
                    Current = job;
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
