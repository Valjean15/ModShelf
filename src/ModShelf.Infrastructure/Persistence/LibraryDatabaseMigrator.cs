using Microsoft.Data.Sqlite;
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
/// therefore always find the schema ready, whatever order they were registered in. A database that can't be opened
/// stops the host with a <see cref="LibraryDatabaseUnavailableException"/>, before anything else runs.
/// </remarks>
internal sealed partial class LibraryDatabaseMigrator(
    LibraryDatabaseOptions options,
    IDbContextFactory<ModShelfDbContext> contextFactory,
    ILogger<LibraryDatabaseMigrator> logger) : IHostedLifecycleService
{
    // SQLite's primary result codes (https://sqlite.org/rescode.html).
    private const int SqlitePermission = 3;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteReadOnly = 8;
    private const int SqliteCorrupt = 11;
    private const int SqliteFull = 13;
    private const int SqliteCantOpen = 14;
    private const int SqliteAuthorization = 23;
    private const int SqliteNotADatabase = 26;

    /// <exception cref="LibraryDatabaseUnavailableException">The database can't be opened or brought up to date.</exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
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
        catch (SqliteException exception)
        {
            throw Unavailable(Problem(exception), exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unavailable($"its folder can't be created. {exception.Message}", exception);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// What's wrong with the database, from SQLite's error, and what the user can do about it.
    /// </summary>
    internal static string Problem(SqliteException exception) => exception.SqliteErrorCode switch
    {
        SqliteNotADatabase or SqliteCorrupt
            => "it isn't a ModShelf library database, or it's damaged. ModShelf leaves it as it is: restore it from a backup, or move it "
                + "elsewhere to start a new library.",
        SqliteBusy or SqliteLocked => "another program is using it. Close that program, then try again.",
        SqliteCantOpen or SqlitePermission or SqliteAuthorization
            => "it can't be opened. Check that you can read and write it, and its folder.",
        SqliteReadOnly => "it can't be written. Check that you can write it, and its folder.",
        SqliteFull => "the disk is full. Make room, then try again.",
        _ => exception.Message,
    };

    private LibraryDatabaseUnavailableException Unavailable(string problem, Exception exception)
    {
        LogUnavailable(logger, exception, options.DatabasePath);
        return new LibraryDatabaseUnavailableException($"The library database {options.DatabasePath} can't be opened: {problem}", exception);
    }
}
