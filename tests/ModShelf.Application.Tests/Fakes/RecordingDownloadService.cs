using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A download queue that only lists and adds jobs, recording what was added.
/// </summary>
internal sealed class RecordingDownloadService(params DownloadJobSnapshot[] existingJobs) : IDownloadService
{
    private readonly List<DownloadJobSnapshot> _jobs = [.. existingJobs];

    public List<DownloadJobSnapshot> Enqueued { get; } = [];

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
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DownloadJobSnapshot>>([.. _jobs]);

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
