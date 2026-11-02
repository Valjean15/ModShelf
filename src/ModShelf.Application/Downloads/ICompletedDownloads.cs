using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Clears chosen completed downloads from the queue, as <see cref="IDownloadService.ClearFinishedAsync"/> clears every
/// finished one: for the library, which would otherwise record a forgotten mod's downloads again.
/// </summary>
internal interface ICompletedDownloads
{
    /// <summary>
    /// Removes the jobs with these IDs that have completed from the queue and the database. Their files stay. An ID of no
    /// job, or of one that hasn't completed, is skipped.
    /// </summary>
    /// <remarks>
    /// A process that doesn't run the queue, such as the CLI's, or one while another runs it, removes them from the
    /// database only: the process that runs the queue lists them until it stops or clears them.
    /// </remarks>
    /// <returns>How many it removed.</returns>
    Task<int> ClearCompletedAsync(IReadOnlySet<DownloadJobId> ids, CancellationToken cancellationToken);
}
