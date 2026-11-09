using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// Keeps mods' details in the library database, one row per mod. Each call uses its own short-lived context.
/// </summary>
internal sealed partial class ModDetailsRepository : IModDetailsRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory;
    private readonly ILogger<ModDetailsRepository> _logger;

    public ModDetailsRepository(IDbContextFactory<ModShelfDbContext> contextFactory, ILogger<ModDetailsRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <exception cref="InvalidDataException">The saved row holds values no details can have.</exception>
    public async Task<SavedModDetails?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        string source = modRef.Game.Source.Value;
        string game = modRef.Game.Key;
        string id = modRef.Id;
        ModDetailsRecord? record;
        try
        {
            await ThreadPoolSwitch.Now;

            ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                record = await context.ModDetails
                    .AsNoTracking()
                    .FirstOrDefaultAsync(row => row.SourceId == source && row.GameKey == game && row.ModId == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (SqliteException exception)
        {
            // Another program holding the database past SQLite's wait, or a damaged file: the site is asked instead.
            LogCantFind(_logger, id, game, source, LibraryDatabaseMigrator.Problem(exception));
            return null;
        }

        return record?.ToSaved();
    }

    public async Task SaveAsync(SavedModDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        ModRef mod = details.Details.Mod;
        try
        {
            await ThreadPoolSwitch.Now;

            ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                ModDetailsRecord? stored = await context.ModDetails
                    .FindAsync([mod.Game.Source.Value, mod.Game.Key, mod.Id], cancellationToken)
                    .ConfigureAwait(false);
                if (stored is null)
                {
                    context.ModDetails.Add(ModDetailsRecord.From(details));
                }
                else
                {
                    stored.CopyFrom(details);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (SqliteProblem(exception) is { } database)
        {
            // Busy, full, or saved meanwhile by another ModShelf: the details are read from the site again next time.
            LogCantSave(_logger, mod.Id, mod.Game.Key, mod.Game.Source.Value, LibraryDatabaseMigrator.Problem(database));
        }
    }

    private static SqliteException? SqliteProblem(Exception exception)
        => exception as SqliteException ?? (exception as DbUpdateException)?.InnerException as SqliteException;
}
