using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The download queue as the UI and the CLI see it: add jobs, control them, and watch their progress.
/// (Named a service, not a queue, because CA1711 reserves the "Queue" suffix for collection types.)
/// </summary>
/// <remarks>
/// Methods that take a job ID throw <see cref="KeyNotFoundException"/> for an unknown job, and
/// <see cref="InvalidOperationException"/> when the job's status doesn't allow the action. While another process runs
/// the queue (<see cref="IsQueueBusyElsewhere"/>), every method but listing and watching throws
/// <see cref="DownloadQueueBusyException"/>.
/// </remarks>
public interface IDownloadService
{
    /// <summary>
    /// Whether another process, such as the desktop app or another CLI command, runs this library's download queue now.
    /// Until it lets go, this one lists the jobs as they were saved, but can't add, hand off or change any, and nothing
    /// downloads here. It's known once the host has started.
    /// </summary>
    bool IsQueueBusyElsewhere { get; }

    /// <summary>
    /// Completes once this process runs the queue: at once when it does already, or once the other process lets go.
    /// </summary>
    Task WhenQueueRunsHereAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a job for <paramref name="file"/>, saved as <paramref name="fileName"/> in
    /// <paramref name="targetDirectory"/> (an absolute path). The scheduler starts it when a slot is free.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The arguments aren't a valid job, or an unfinished job saves to the same place already.
    /// </exception>
    Task<DownloadJobSnapshot> EnqueueAsync(
        ModFileRef file,
        string targetDirectory,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives the queue the link that completes a handoff for <paramref name="file"/>: the link the source's
    /// website handed over after the user started the download there (see <see cref="DownloadJobSnapshot.HandoffPage"/>).
    /// A job for the file that is waiting for it resolves again with the link, and so does a job for the file
    /// added or resumed later.
    /// </summary>
    /// <remarks>
    /// The link can carry a key, so it's kept in memory only and never logged. It's dropped once the source
    /// refuses it, or once no unfinished job for the file is left.
    /// </remarks>
    Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken);

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
    /// Removes the completed and cancelled jobs from the queue, and from the database. Their files stay, and the
    /// library keeps its own records of the ones it has. The jobs aren't listed or watched any more.
    /// </summary>
    /// <returns>How many jobs it removed.</returns>
    Task<int> ClearFinishedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A snapshot every time a job changes: its status, or its progress (at most a few times per second).
    /// The stream ends when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken);
}
