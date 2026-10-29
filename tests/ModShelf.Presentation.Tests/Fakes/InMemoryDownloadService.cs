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
    private readonly ControlledWatchers<DownloadJobSnapshot> _watchers = new();

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
    public Task WaitedOn => _watchers.WaitedOn;

    /// <summary>
    /// Completed once the queue runs here; a test that sets another process running it replaces it.
    /// </summary>
    public TaskCompletionSource RunsHere { get; set; } = CompletedRunsHere();

    public bool IsQueueBusyElsewhere => !RunsHere.Task.IsCompleted;

    public Task WhenQueueRunsHereAsync(CancellationToken cancellationToken) => RunsHere.Task.WaitAsync(cancellationToken);

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
        }

        _watchers.Publish(job);
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

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken) => _watchers.Watch(cancellationToken);

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

    private static TaskCompletionSource CompletedRunsHere()
    {
        TaskCompletionSource done = new();
        done.SetResult();
        return done;
    }
}
