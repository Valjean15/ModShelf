using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A download queue that only lists jobs, adds them and takes handoff links, recording what it was given. The test
/// changes jobs with <see cref="Update"/>, which one watcher sees.
/// </summary>
internal sealed class RecordingDownloadService(params DownloadJobSnapshot[] existingJobs) : IDownloadService
{
    private readonly Lock _lock = new();
    private readonly List<DownloadJobSnapshot> _jobs = [.. existingJobs];
    private readonly Channel<DownloadJobSnapshot> _updates = Channel.CreateUnbounded<DownloadJobSnapshot>();

    public List<DownloadJobSnapshot> Enqueued { get; } = [];

    public bool IsQueueBusyElsewhere => false;

    public List<(ModFileRef File, Uri Link)> HandedOff { get; } = [];

    /// <summary>
    /// Whether anything has started reading <see cref="WatchAsync"/>.
    /// </summary>
    public bool Watched { get; private set; }

    /// <summary>
    /// The names of the methods that changed something, in the order they were called.
    /// </summary>
    public List<string> Changes { get; } = [];

    /// <summary>
    /// A job in <paramref name="status"/>, as the scheduler would list it.
    /// </summary>
    public static DownloadJobSnapshot Job(ModFileRef file, string targetDirectory, string fileName, DownloadJobStatus status)
        => new(
            DownloadJobId.New(),
            file,
            targetDirectory,
            fileName,
            status,
            BytesDownloaded: 0,
            TotalBytes: null,
            FailureReason: status == DownloadJobStatus.Failed ? DownloadFailureReason.Network : null,
            TestData.Now);

    /// <summary>
    /// The job finished: as <paramref name="job"/> with <paramref name="sizeInBytes"/> downloaded.
    /// </summary>
    public static DownloadJobSnapshot Completed(DownloadJobSnapshot job, long sizeInBytes = 100)
        => job with { Status = DownloadJobStatus.Completed, BytesDownloaded = sizeInBytes, TotalBytes = sizeInBytes };

    /// <summary>
    /// Adds a job that was there before anything watched, so no watcher sees it.
    /// </summary>
    public void Add(DownloadJobSnapshot job)
    {
        lock (_lock)
        {
            _jobs.Add(job);
        }
    }

    /// <summary>
    /// Replaces the job with the same ID, or adds it, and tells the watcher.
    /// </summary>
    public void Update(DownloadJobSnapshot job)
    {
        lock (_lock)
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

        _updates.Writer.TryWrite(job);
    }

    /// <summary>
    /// Ends the watch, as the scheduler does when it stops.
    /// </summary>
    public void Stop() => _updates.Writer.TryComplete();

    public Task<DownloadJobSnapshot> EnqueueAsync(ModFileRef file, string targetDirectory, string fileName, CancellationToken cancellationToken)
    {
        DownloadJobSnapshot job = Job(file, targetDirectory, fileName, DownloadJobStatus.Queued);
        lock (_lock)
        {
            _jobs.Add(job);
        }

        Enqueued.Add(job);
        Changes.Add(nameof(EnqueueAsync));
        return Task.FromResult(job);
    }

    public Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken)
    {
        HandedOff.Add((file, handoff));
        Changes.Add(nameof(HandOffAsync));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DownloadJobSnapshot>>([.. _jobs]);
        }
    }

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> ClearFinishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>
    /// The jobs <see cref="Update"/> changes from now on, until <see cref="Stop"/>. One watcher at a time.
    /// </summary>
    public async IAsyncEnumerable<DownloadJobSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Watched = true;
        while (await WaitAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_updates.Reader.TryRead(out DownloadJobSnapshot? job))
            {
                yield return job;
            }
        }
    }

    /// <summary>
    /// Waits for a change; false when the watch ends, as cancelling the scheduler's watch ends it.
    /// </summary>
    private async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _updates.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public Task WhenQueueRunsHereAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
