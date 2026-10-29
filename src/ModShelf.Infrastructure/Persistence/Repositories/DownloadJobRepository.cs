using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores download jobs in the library database. Each call uses its own short-lived context.
/// </summary>
internal sealed partial class DownloadJobRepository : IDownloadJobRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory;
    private readonly ILogger<DownloadJobRepository> _logger;

    public DownloadJobRepository(IDbContextFactory<ModShelfDbContext> contextFactory, ILogger<DownloadJobRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
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

    /// <summary>
    /// Every stored job, oldest first. A row that holds values no job can have, such as a status a newer ModShelf
    /// wrote, is left out and logged, so one row doesn't keep the rest of the queue from being read.
    /// </summary>
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

            List<DownloadJob> jobs = new(records.Count);
            foreach (DownloadJobRecord record in records)
            {
                try
                {
                    jobs.Add(record.ToJob());
                }
                catch (InvalidDataException exception)
                {
                    LogJobLeftOut(_logger, exception, record.Id);
                }
            }

            return jobs;
        }
    }

    public async Task DeleteAsync(IReadOnlyCollection<DownloadJobId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return;
        }

        Guid[] keys = [.. ids.Select(id => id.Value)];
        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.DownloadJobs
                .Where(record => keys.Contains(record.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
