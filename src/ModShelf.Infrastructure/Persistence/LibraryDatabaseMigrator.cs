using System.Diagnostics;

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
/// <para>
/// An update holds a lock of its own: a file next to the database, held open with no sharing, as the download queue's
/// lock is, which the operating system lets go of however the process ends. Another ModShelf starting meanwhile waits
/// for it. While it's held, no other ModShelf is updating the database, so a lock that EF Core's own update left in the
/// database when it stopped part-way, which would make every later update wait for good, is cleared.
/// </para>
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

    // EF Core's lock on updating a SQLite database: a row in this table while an update runs.
    private const string UpdateLockTable = "__EFMigrationsLock";

    private static readonly TimeSpan UpdateLockRetry = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long a start waits for another ModShelf updating the database.
    /// </summary>
    internal TimeSpan UpdatePatience { get; init; } = TimeSpan.FromSeconds(30);

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
                if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
                {
                    FileStream updating = await TakeUpdateLockAsync(cancellationToken).ConfigureAwait(false);
                    await using (updating.ConfigureAwait(false))
                    {
                        await UpdateAsync(context, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                LogUpToDate(logger);
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

    /// <summary>
    /// Applies the migrations still pending, with the update lock held: none, when another ModShelf applied them while
    /// this one waited for it.
    /// </summary>
    private async Task UpdateAsync(ModShelfDbContext context, CancellationToken cancellationToken)
    {
        List<string> pending = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
        if (pending.Count == 0)
        {
            LogUpToDate(logger);
            return;
        }

        bool locked = await context.Database
            .SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {UpdateLockTable}")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
        if (locked && await context.Database.ExecuteSqlRawAsync($"DELETE FROM \"{UpdateLockTable}\"", cancellationToken).ConfigureAwait(false) > 0)
        {
            LogClearedStoppedUpdate(logger);
        }

        LogApplyingMigrations(logger, pending.Count, pending[^1]);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the update lock, waiting up to <see cref="UpdatePatience"/> while another ModShelf holds it.
    /// </summary>
    /// <exception cref="LibraryDatabaseUnavailableException">Another ModShelf held it all that time.</exception>
    private async Task<FileStream> TakeUpdateLockAsync(CancellationToken cancellationToken)
    {
        string path = options.DatabasePath + ".update.lock";
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                if (Stopwatch.GetElapsedTime(started) >= UpdatePatience)
                {
                    throw Unavailable("another ModShelf is updating it, and hasn't finished. Wait for it, then try again.", exception);
                }
            }

            await Task.Delay(UpdateLockRetry, cancellationToken).ConfigureAwait(false);
        }
    }

    private LibraryDatabaseUnavailableException Unavailable(string problem, Exception exception)
    {
        LogUnavailable(logger, exception, options.DatabasePath);
        return new LibraryDatabaseUnavailableException($"The library database {options.DatabasePath} can't be opened: {problem}", exception);
    }
}
