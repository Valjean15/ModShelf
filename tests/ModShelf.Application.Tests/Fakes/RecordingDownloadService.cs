using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A download queue that only lists jobs, adds them and takes handoff links, recording what it was given.
/// </summary>
internal sealed class RecordingDownloadService(params DownloadJobSnapshot[] existingJobs) : IDownloadService
{
    private readonly List<DownloadJobSnapshot> _jobs = [.. existingJobs];

    public List<DownloadJobSnapshot> Enqueued { get; } = [];

    public List<(ModFileRef File, Uri Link)> HandedOff { get; } = [];

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

    public Task<DownloadJobSnapshot> EnqueueAsync(ModFileRef file, string targetDirectory, string fileName, CancellationToken cancellationToken)
    {
        DownloadJobSnapshot job = Job(file, targetDirectory, fileName, DownloadJobStatus.Queued);
        _jobs.Add(job);
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
        => Task.FromResult<IReadOnlyList<DownloadJobSnapshot>>([.. _jobs]);

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
