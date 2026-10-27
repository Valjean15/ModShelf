using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// Stores download jobs so they survive restarts. Implemented by Infrastructure.
/// </summary>
public interface IDownloadJobRepository
{
    Task AddAsync(DownloadJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the job's current state.
    /// </summary>
    Task UpdateAsync(DownloadJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Every stored job, oldest first.
    /// </summary>
    Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the jobs with these IDs. An ID of no stored job is skipped.
    /// </summary>
    Task DeleteAsync(IReadOnlyCollection<DownloadJobId> ids, CancellationToken cancellationToken);
}
