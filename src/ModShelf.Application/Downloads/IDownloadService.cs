using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The download queue as the UI and the CLI see it: add jobs, control them, and watch their progress.
/// (Named a service, not a queue, because CA1711 reserves the "Queue" suffix for collection types.)
/// </summary>
/// <remarks>
/// Methods that take a job ID throw <see cref="KeyNotFoundException"/> for an unknown job, and
/// <see cref="InvalidOperationException"/> when the job's status doesn't allow the action.
/// </remarks>
public interface IDownloadService
{
    /// <summary>
    /// Adds a job for <paramref name="file"/>, saved as <paramref name="fileName"/> in
    /// <paramref name="targetDirectory"/> (an absolute path). The scheduler starts it when a slot is free.
    /// </summary>
    Task<DownloadJobSnapshot> EnqueueAsync(
        ModFileRef file,
        string targetDirectory,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every job, oldest first.
    /// </summary>
    Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken);

    Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken);

    Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken);

    Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the job and deletes the partial file it left behind. A finished file is never deleted.
    /// </summary>
    Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken);

    /// <summary>
    /// A snapshot every time a job changes: its status, or its progress (at most a few times per second).
    /// The stream ends when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken);
}
