using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// The library database writes ahead of its file (SQLite's WAL): a read never waits for a change, nor a change for a
/// read, as the desktop app's screens did while the command line's MCP server changed the library (ADR 0082).
/// </summary>
public sealed class WriteAheadTests : IAsyncLifetime
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 13, 28, 0, TimeSpan.Zero);

    // Far less than SQLite's wait for a lock, 30 seconds.
    private static readonly TimeSpan Quickly = TimeSpan.FromSeconds(5);

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task ANewDatabaseWritesAhead()
    {
        Assert.Equal("wal", await _database.QueryAsync("PRAGMA journal_mode"));
        Assert.Contains(_database.Logs.Entries, entry => entry.Contains("The library database now writes ahead of its file (WAL)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADatabaseOfAnEarlierVersionWritesAheadOnceOpenedKeepingWhatItHas()
    {
        DownloadJob job = Job("1");
        await _database.Repository.AddAsync(job, Cancellation);
        await JournalAsync("delete");

        await _database.RestartAsync();

        Assert.Equal("wal", await _database.QueryAsync("PRAGMA journal_mode"));
        Assert.Equal(job.Id, Assert.Single(await _database.Repository.ListAsync(Cancellation)).Id);
    }

    [Fact]
    public async Task AChangeDoesntWaitForAnotherConnectionsRead()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);
        await using SqliteConnection reader = await OpenAsync();
        await using SqliteTransaction reading = reader.BeginTransaction(deferred: true);
        await ExecuteAsync(reader, reading, "SELECT COUNT(*) FROM DownloadJobs");

        // On a thread of its own: were it to wait, SQLite would block the thread it runs on.
        await Task.Run(() => _database.Repository.AddAsync(Job("2"), Cancellation), Cancellation).WaitAsync(Quickly, Cancellation);

        Assert.Equal(1L, await ExecuteAsync(reader, reading, "SELECT COUNT(*) FROM DownloadJobs"));
        await reading.CommitAsync(Cancellation);
        Assert.Equal(2, (await _database.Repository.ListAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task AReadDoesntWaitForAnotherConnectionsChange()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);
        await using SqliteConnection writer = await OpenAsync();

        // An exclusive lock, which keeps every reader out of a database that doesn't write ahead.
        await ExecuteAsync(writer, null, "BEGIN EXCLUSIVE");
        await ExecuteAsync(writer, null, "DELETE FROM DownloadJobs");

        IReadOnlyList<DownloadJob> jobs = await Task.Run(() => _database.Repository.ListAsync(Cancellation), Cancellation).WaitAsync(Quickly, Cancellation);

        Assert.Single(jobs);
        await ExecuteAsync(writer, null, "COMMIT");
    }

    [Fact]
    public async Task WhileAnotherProgramHoldsTheDatabaseItKeepsItsJournalUntilTheNextStart()
    {
        await JournalAsync("delete");
        RecordingLogger<LibraryDatabaseMigrator> logger = new();
        await using SqliteConnection reader = await OpenAsync();
        await using (SqliteTransaction reading = reader.BeginTransaction(deferred: true))
        {
            await ExecuteAsync(reader, reading, "SELECT COUNT(*) FROM DownloadJobs");

            // Waiting a second for it, not SQLite's 30.
            await Migrator(logger, waitSeconds: 1).StartingAsync(Cancellation);

            Assert.Equal("delete", await ExecuteAsync(reader, reading, "PRAGMA journal_mode"));
            Assert.Contains(logger.Entries, entry => entry.StartsWith("Warning: The library database couldn't start writing ahead of its file (WAL) while another program used it", StringComparison.Ordinal));
        }

        await Migrator(logger, waitSeconds: 1).StartingAsync(Cancellation);

        Assert.Equal("wal", await _database.QueryAsync("PRAGMA journal_mode"));
    }

    /// <summary>
    /// A job for a file of SMAPI's, saved beside the database.
    /// </summary>
    private DownloadJob Job(string fileId)
        => DownloadJob.Create(new ModFileRef(Smapi, fileId), Path.GetDirectoryName(_database.DatabasePath)!, $"SMAPI {fileId}.zip", Now);

    /// <summary>
    /// Puts the database back in another journal, as ModShelf made it before 1.0.1, with every connection closed.
    /// </summary>
    private async Task JournalAsync(string mode)
    {
        SqliteConnection.ClearAllPools();
        Assert.Equal(mode, await _database.QueryAsync($"PRAGMA journal_mode = {mode}"));
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(Cancellation);
        return connection;
    }

    private static async Task<object?> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Cancellation);
    }

    /// <summary>
    /// The library's start on its own, with SQLite waiting <paramref name="waitSeconds"/> for another connection's lock.
    /// </summary>
    private LibraryDatabaseMigrator Migrator(RecordingLogger<LibraryDatabaseMigrator> logger, int waitSeconds)
        => new(
            new LibraryDatabaseOptions(_database.DatabasePath),
            new PooledDbContextFactory<ModShelfDbContext>(new DbContextOptionsBuilder<ModShelfDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, DefaultTimeout = waitSeconds, Pooling = false }.ToString())
                .AddInterceptors(new SqliteBusyTimeout())
                .Options),
            logger);
}
