using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Creates the library database, or brings its schema up to date, when the host starts.
/// </summary>
/// <remarks>
/// The work happens in <see cref="StartingAsync"/>, which the host runs before any hosted service's
/// <c>StartAsync</c>. Services that read the database at startup, such as the download scheduler,
/// therefore always find the schema ready, whatever order they were registered in.
/// </remarks>
internal sealed partial class LibraryDatabaseMigrator(
    LibraryDatabaseOptions options,
    IDbContextFactory<ModShelfDbContext> contextFactory,
    ILogger<LibraryDatabaseMigrator> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // SQLite creates the file but not its folder.
        string? folder = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        ModShelfDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<string> pending = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
            if (pending.Count == 0)
            {
                LogUpToDate(logger);
                return;
            }

            LogApplyingMigrations(logger, pending.Count, pending[^1]);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
