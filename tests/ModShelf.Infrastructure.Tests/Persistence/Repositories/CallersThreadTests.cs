using System.Collections.Concurrent;
using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using ModShelf.Application.Accounts;
using ModShelf.Application.Catalog.Ports;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Repositories;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Every repository's work runs on the thread pool, never on the thread that calls it, such as a screen's UI thread,
/// which Microsoft.Data.Sqlite would otherwise hold while it reads and writes the database, and while it waits for
/// another connection's lock (ADR 0083).
/// </summary>
public sealed class CallersThreadTests : IAsyncLifetime
{
    private static readonly SourceId Nexus = new("nexus");
    private static readonly ModRef Smapi = new(new GameRef(Nexus, "stardewvalley"), "2400");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 13, 28, 0, TimeSpan.Zero);

    private readonly TestLibraryDatabase _database = new();
    private readonly DownloadJob _job;

    public CallersThreadTests()
    {
        _job = DownloadJob.Create(new ModFileRef(Smapi, "1"), Path.GetDirectoryName(_database.DatabasePath)!, "SMAPI 1.zip", Now);
    }

    public static TheoryData<string> Calls =>
    [
        "DownloadJobs.Add", "DownloadJobs.Update", "DownloadJobs.List", "DownloadJobs.Delete",
        "LinkedAccounts.List", "LinkedAccounts.Save", "LinkedAccounts.Delete",
        "Library.List", "Library.ListGames", "Library.Find", "Library.Update",
        "ModDetails.Find", "ModDetails.Save",
        "History.Add", "History.List",
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await _database.Repository.AddAsync(_job, Cancellation);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task NoCallUsesTheDatabaseOnTheCallersThread(string call)
    {
        ThreadsOpening contexts = new(_database.DatabasePath);

        using (CallingThread caller = new(() => Call(contexts, call)))
        {
            await caller.Work;

            Assert.NotEmpty(contexts.Threads);
            Assert.DoesNotContain(caller.Id, contexts.Threads);
        }
    }

    [Fact]
    public async Task ACallReturnsToItsCallerWhileItWaitsForAnotherConnection()
    {
        await using SqliteConnection other = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString());
        await other.OpenAsync(Cancellation);
        await using SqliteTransaction changing = other.BeginTransaction(deferred: false);

        using CallingThread caller = new(() => _database.Repository.AddAsync(Job("2"), Cancellation));
        await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        Assert.False(caller.Work.IsCompleted, "The call didn't wait for the other connection.");
        await changing.CommitAsync(Cancellation);

        await caller.Work.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.True(caller.Calling < TimeSpan.FromMilliseconds(400), $"The call held its caller for {caller.Calling}.");
    }

    private DownloadJob Job(string fileId)
        => DownloadJob.Create(new ModFileRef(Smapi, fileId), Path.GetDirectoryName(_database.DatabasePath)!, $"SMAPI {fileId}.zip", Now);

    private Task Call(ThreadsOpening contexts, string call)
    {
        DownloadJobRepository jobs = new(contexts, new RecordingLogger<DownloadJobRepository>());
        LinkedAccountRepository accounts = new(contexts);
        LibraryRepository library = new(contexts);
        ModDetailsRepository details = new(contexts, new RecordingLogger<ModDetailsRepository>());
        LibraryHistoryRepository history = new(contexts);
        return call switch
        {
            "DownloadJobs.Add" => jobs.AddAsync(Job("2"), Cancellation),
            "DownloadJobs.Update" => jobs.UpdateAsync(_job, Cancellation),
            "DownloadJobs.List" => jobs.ListAsync(Cancellation),
            "DownloadJobs.Delete" => jobs.DeleteAsync([_job.Id], Cancellation),
            "LinkedAccounts.List" => accounts.ListAsync(Cancellation),
            "LinkedAccounts.Save" => accounts.SaveAsync(new LinkedAccount(Nexus, AuthKind.ApiKey, new AccountProfile("Jean"), Now), Cancellation),
            "LinkedAccounts.Delete" => accounts.DeleteAsync(Nexus, Cancellation),
            "Library.List" => library.ListAsync(Cancellation),
            "Library.ListGames" => library.ListGamesAsync(Cancellation),
            "Library.Find" => library.FindAsync(Smapi, Cancellation),
            "Library.Update" => library.UpdateAsync(Smapi, _ => LibraryMod.Watch(Smapi, "SMAPI", null, null, Now), Cancellation),
            "ModDetails.Find" => details.FindAsync(Smapi, Cancellation),
            "ModDetails.Save" => details.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), Now), Cancellation),
            "History.Add" => history.AddAsync([new LibraryEvent(Now, LibraryEventKind.Removed, Smapi, "SMAPI")], Now.AddYears(-1), Cancellation),
            "History.List" => history.ListAsync(null, 10, Cancellation),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "No such call."),
        };
    }

    /// <summary>
    /// Contexts on the test's database that note the thread each connection is opened on.
    /// </summary>
    private sealed class ThreadsOpening(string databasePath) : DbConnectionInterceptor, IDbContextFactory<ModShelfDbContext>
    {
        private readonly ConcurrentQueue<int> _threads = new();

        public IReadOnlyCollection<int> Threads => _threads;

        public ModShelfDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<ModShelfDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())
                .AddInterceptors(this)
                .Options);

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            _threads.Enqueue(Environment.CurrentManagedThreadId);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _threads.Enqueue(Environment.CurrentManagedThreadId);
            return ValueTask.FromResult(result);
        }
    }
}
