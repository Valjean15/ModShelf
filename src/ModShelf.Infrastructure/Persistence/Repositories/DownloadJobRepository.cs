using Microsoft.EntityFrameworkCore;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores download jobs in the library database. Each call uses its own short-lived context.
/// </summary>
internal sealed class DownloadJobRepository : IDownloadJobRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory;

    public DownloadJobRepository(IDbContextFactory<ModShelfDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        _contextFactory = contextFactory;
    }

    public async Task AddAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            context.DownloadJobs.Add(DownloadJobRecord.From(job));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Saves the parts of a job that change: its status, progress, size and failure reason.
    /// </summary>
    /// <exception cref="InvalidOperationException">The job was never added.</exception>
    public async Task UpdateAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        Guid id = job.Id.Value;
        DownloadJobStatus status = job.Status;
        long bytesDownloaded = job.BytesDownloaded;
        long? totalBytes = job.TotalBytes;
        DownloadFailureReason? failureReason = job.FailureReason;

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            int updated = await context.DownloadJobs
                .Where(record => record.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(record => record.Status, status)
                        .SetProperty(record => record.BytesDownloaded, bytesDownloaded)
                        .SetProperty(record => record.TotalBytes, totalBytes)
                        .SetProperty(record => record.FailureReason, failureReason),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                throw new InvalidOperationException($"Download job {job.Id} isn't stored, so it can't be updated.");
            }
        }
    }

    /// <exception cref="InvalidDataException">A stored row holds values no job can have.</exception>
    public async Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken)
    {
        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<DownloadJobRecord> records = await context.DownloadJobs
                .AsNoTracking()
                .OrderBy(record => record.Sequence)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. records.Select(record => record.ToJob())];
        }
    }
}
