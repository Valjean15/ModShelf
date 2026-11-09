using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// Changes to the library database while another connection changes it, as another ModShelf, or a change in this one,
/// does: SQLite lets one connection change the file at a time, so the change waits for the other to end. Since the
/// database writes ahead (ADR 0082), a read no longer holds a change: <see cref="WriteAheadTests"/>.
/// </summary>
public sealed class LibraryDatabaseWaitTests : IAsyncLifetime
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 20, 48, 25, TimeSpan.Zero);

    // Long enough that a change that doesn't wait has failed by then.
    private static readonly TimeSpan ChangeFor = TimeSpan.FromMilliseconds(500);

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AJobAddedDuringAnotherChangeIsSavedOnceItEnds()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);
        DownloadJob job = Job("2");

        await WhileChangingAsync(() => _database.Repository.AddAsync(job, Cancellation));

        Assert.Contains(job.Id, (await _database.Repository.ListAsync(Cancellation)).Select(stored => stored.Id));
    }

    [Fact]
    public async Task AJobChangedDuringAnotherChangeIsSavedOnceItEnds()
    {
        DownloadJob job = Job("1");
        await _database.Repository.AddAsync(job, Cancellation);
        job.StartResolving();
        await _database.Repository.UpdateAsync(job, Cancellation);
        job.StartDownloading(resumeFromByte: 0, totalBytes: 1_024);

        await WhileChangingAsync(() => _database.Repository.UpdateAsync(job, Cancellation));

        Assert.Equal(DownloadJobStatus.Downloading, Assert.Single(await _database.Repository.ListAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task AChangeSavedWithoutAwaitingWaitsToo()
    {
        IDbContextFactory<ModShelfDbContext> contexts = _database.Services.GetRequiredService<IDbContextFactory<ModShelfDbContext>>();
        void Save(DownloadJob job)
        {
            // Saving without awaiting opens the connection without awaiting too.
            using ModShelfDbContext context = contexts.CreateDbContext();
            context.DownloadJobs.Add(DownloadJobRecord.From(job));
            context.SaveChanges();
        }

        // Starting the database opened its connections awaiting; these are opened anew, without.
        SqliteConnection.ClearAllPools();
        Save(Job("1"));
        DownloadJob job = Job("2");

        await WhileChangingAsync(() => Task.Run(() => Save(job), Cancellation));

        Assert.Contains(job.Id, (await _database.Repository.ListAsync(Cancellation)).Select(stored => stored.Id));
    }

    [Fact]
    public async Task AModAddedDuringAnotherChangeIsSavedOnceItEnds()
    {
        ModRef contentPatcher = new(Smapi.Game, "1915");
        await _database.Library.UpdateAsync(contentPatcher, _ => LibraryMod.Watch(contentPatcher, "Content Patcher", null, null, Now), Cancellation);

        await WhileChangingAsync(() => _database.Library.UpdateAsync(Smapi, _ => LibraryMod.Watch(Smapi, "SMAPI", null, null, Now), Cancellation));

        Assert.Contains("SMAPI", (await _database.Library.ListAsync(Cancellation)).Select(mod => mod.Name));
    }

    /// <summary>
    /// A job for a file of SMAPI's, saved beside the database.
    /// </summary>
    private DownloadJob Job(string fileId)
        => DownloadJob.Create(new ModFileRef(Smapi, fileId), Path.GetDirectoryName(_database.DatabasePath)!, $"SMAPI {fileId}.zip", Now);

    /// <summary>
    /// Runs <paramref name="change"/> while another connection holds a change of the database, and ends that after
    /// <see cref="ChangeFor"/>: the change is still waiting then, and done once the other ends. Each test makes the same
    /// kind of change once before, so the change reaches the database within that time rather than spending it starting
    /// up.
    /// </summary>
    private async Task WhileChangingAsync(Func<Task> change)
    {
        await using SqliteConnection other = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString());
        await other.OpenAsync(Cancellation);
        await using SqliteTransaction changingToo = other.BeginTransaction(deferred: false);
        await using (SqliteCommand write = other.CreateCommand())
        {
            write.Transaction = changingToo;
            write.CommandText = "DELETE FROM DownloadJobs WHERE 0";
            await write.ExecuteNonQueryAsync(Cancellation);
        }

        // On a thread of its own: SQLite waits for the other change by blocking the thread it runs on.
        Task changing = Task.Run(change, Cancellation);
        await Task.WhenAny(changing, Task.Delay(ChangeFor, Cancellation));
        Assert.False(changing.IsCompleted, changing.Exception?.ToString() ?? "The change didn't wait for the other.");

        await changingToo.CommitAsync(Cancellation);
        await changing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
    }
}
