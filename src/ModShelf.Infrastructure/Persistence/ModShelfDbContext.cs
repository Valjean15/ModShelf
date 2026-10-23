using Microsoft.EntityFrameworkCore;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database: one SQLite file holding what ModShelf keeps between runs. Secrets never go here;
/// they belong in the OS secret store.
/// </summary>
/// <remarks>
/// Create one per unit of work through <see cref="IDbContextFactory{TContext}"/>; a context isn't thread-safe.
/// </remarks>
public sealed class ModShelfDbContext(DbContextOptions<ModShelfDbContext> options) : DbContext(options)
{
    internal DbSet<DownloadJobRecord> DownloadJobs => Set<DownloadJobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new DownloadJobRecordConfiguration());
    }
}
