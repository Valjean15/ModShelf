using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Tables;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// A library database command that takes a second or more, as one waiting for another process's lock does, is logged:
/// how long, and what it was, never its values (ADR 0081).
/// </summary>
public sealed partial class SlowCommandLogTests : IAsyncLifetime
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 13, 28, 0, TimeSpan.Zero);

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AChangeThatWaitsASecondForAnotherWriteIsLogged()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);

        await WhileWritingAsync(TimeSpan.FromMilliseconds(1_300), () => _database.Repository.AddAsync(Job("2"), Cancellation));

        string logged = Assert.Single(Slow());
        Assert.Matches(@"The library database took [1-9]\d*\.\d s for INSERT into DownloadJobs \|", logged);
        Assert.DoesNotContain("SMAPI", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeThatWaitsToStartItsTransactionIsLogged()
    {
        ModRef contentPatcher = new(Smapi.Game, "1915");
        await _database.Library.UpdateAsync(contentPatcher, _ => LibraryMod.Watch(contentPatcher, "Content Patcher", null, null, Now), Cancellation);

        await WhileWritingAsync(TimeSpan.FromMilliseconds(1_300), () => _database.Library.UpdateAsync(Smapi, _ => LibraryMod.Watch(Smapi, "SMAPI", null, null, Now), Cancellation));

        Assert.Matches(@"The library database took [1-9]\d*\.\d s for BEGIN \|", Assert.Single(Slow()));
    }

    [Fact]
    public async Task QuickCommandsAreNotLogged()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);
        await _database.Repository.ListAsync(Cancellation);

        // Waiting less than a second for another write.
        await WhileWritingAsync(TimeSpan.FromMilliseconds(300), () => _database.Repository.AddAsync(Job("2"), Cancellation));

        Assert.Empty(Slow());
    }

    [Fact]
    public async Task ACommandThatFailsAfterWaitingIsLoggedToo()
    {
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggers = LoggerFactory.Create(logging => logging.AddProvider(logs));
        DbContextOptions<ModShelfDbContext> options = new DbContextOptionsBuilder<ModShelfDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, DefaultTimeout = 1, Pooling = false }.ToString())
            .AddInterceptors(new SqliteBusyTimeout(), new SlowCommandLog(loggers.CreateLogger<SlowCommandLog>(), TimeSpan.FromMilliseconds(500)))
            .Options;

        await using (SqliteConnection writer = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString()))
        {
            await writer.OpenAsync(Cancellation);
            await using SqliteTransaction writing = writer.BeginTransaction(deferred: false);
            await using ModShelfDbContext context = new(options);
            context.DownloadJobs.Add(DownloadJobRecord.From(Job("2")));

            // SQLite gives up after the connection's second, or a little more: how much more is each system's own.
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => Task.Run(() => context.SaveChangesAsync(Cancellation), Cancellation));
        }

        Assert.True(
            logs.Entries.Any(entry => FailedAfterWaiting().IsMatch(entry)),
            $"No failed command was logged, only: {string.Join(" / ", logs.Entries)}");
    }

    [GeneratedRegex(@"The library database failed INSERT into DownloadJobs after [1-9]\d*\.\d s \|")]
    private static partial Regex FailedAfterWaiting();

    private IEnumerable<string> Slow()
        => _database.Logs.Entries.Where(entry => entry.StartsWith($"[{typeof(SlowCommandLog).FullName}]", StringComparison.Ordinal));

    /// <summary>
    /// A job for a file of SMAPI's, saved beside the database.
    /// </summary>
    private DownloadJob Job(string fileId)
        => DownloadJob.Create(new ModFileRef(Smapi, fileId), Path.GetDirectoryName(_database.DatabasePath)!, $"SMAPI {fileId}.zip", Now);

    /// <summary>
    /// Runs <paramref name="change"/> while another connection holds a write of the database for
    /// <paramref name="writeFor"/>, which the change waits for, whatever the journal.
    /// </summary>
    private async Task WhileWritingAsync(TimeSpan writeFor, Func<Task> change)
    {
        await using SqliteConnection writer = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString());
        await writer.OpenAsync(Cancellation);
        await using SqliteTransaction writing = writer.BeginTransaction(deferred: false);

        // On a thread of its own: SQLite waits for the write by blocking the thread it runs on.
        Task changing = Task.Run(change, Cancellation);
        await Task.WhenAny(changing, Task.Delay(writeFor, Cancellation));
        Assert.False(changing.IsCompleted, changing.Exception?.ToString() ?? "The change didn't wait for the write.");

        await writing.CommitAsync(Cancellation);
        await changing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
    }
}
