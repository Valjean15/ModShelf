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
/// Changes to the library database while another connection reads it, as another ModShelf, or a read in this one, does:
/// SQLite can't finish a change while a reader holds the file, so the change waits for the read to end.
/// </summary>
public sealed class LibraryDatabaseWaitTests : IAsyncLifetime
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 20, 48, 25, TimeSpan.Zero);

    // Long enough that a change that doesn't wait has failed by then.
    private static readonly TimeSpan ReadFor = TimeSpan.FromMilliseconds(500);

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AJobAddedDuringAReadIsSavedOnceTheReadEnds()
    {
        await _database.Repository.AddAsync(Job("1"), Cancellation);
        DownloadJob job = Job("2");

        await WhileReadingAsync(() => _database.Repository.AddAsync(job, Cancellation));

        Assert.Contains(job.Id, (await _database.Repository.ListAsync(Cancellation)).Select(stored => stored.Id));
    }

    [Fact]
    public async Task AJobChangedDuringAReadIsSavedOnceTheReadEnds()
    {
        DownloadJob job = Job("1");
        await _database.Repository.AddAsync(job, Cancellation);
        job.StartResolving();
        await _database.Repository.UpdateAsync(job, Cancellation);
        job.StartDownloading(resumeFromByte: 0, totalBytes: 1_024);

        await WhileReadingAsync(() => _database.Repository.UpdateAsync(job, Cancellation));

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

        await WhileReadingAsync(() => Task.Run(() => Save(job), Cancellation));

        Assert.Contains(job.Id, (await _database.Repository.ListAsync(Cancellation)).Select(stored => stored.Id));
    }

    [Fact]
    public async Task AModAddedDuringAReadIsSavedOnceTheReadEnds()
    {
        ModRef contentPatcher = new(Smapi.Game, "1915");
        await _database.Library.UpdateAsync(contentPatcher, _ => LibraryMod.Watch(contentPatcher, "Content Patcher", null, null, Now), Cancellation);

        await WhileReadingAsync(() => _database.Library.UpdateAsync(Smapi, _ => LibraryMod.Watch(Smapi, "SMAPI", null, null, Now), Cancellation));

        Assert.Contains("SMAPI", (await _database.Library.ListAsync(Cancellation)).Select(mod => mod.Name));
    }

    /// <summary>
    /// A job for a file of SMAPI's, saved beside the database.
    /// </summary>
    private DownloadJob Job(string fileId)
        => DownloadJob.Create(new ModFileRef(Smapi, fileId), Path.GetDirectoryName(_database.DatabasePath)!, $"SMAPI {fileId}.zip", Now);

    /// <summary>
    /// Runs <paramref name="change"/> while another connection holds a read of the database, and ends the read after
    /// <see cref="ReadFor"/>: the change is still waiting then, and done once the read ends. Each test makes the same kind
    /// of change once before, so the change reaches the database within that time rather than spending it starting up.
    /// </summary>
    private async Task WhileReadingAsync(Func<Task> change)
    {
        await using SqliteConnection reader = new(new SqliteConnectionStringBuilder { DataSource = _database.DatabasePath, Pooling = false }.ToString());
        await reader.OpenAsync(Cancellation);
        await using SqliteTransaction reading = reader.BeginTransaction(deferred: true);
        await using (SqliteCommand read = reader.CreateCommand())
        {
            read.Transaction = reading;
            read.CommandText = "SELECT COUNT(*) FROM DownloadJobs";
            await read.ExecuteScalarAsync(Cancellation);
        }

        // On a thread of its own: SQLite waits for the read by blocking the thread it runs on.
        Task changing = Task.Run(change, Cancellation);
        await Task.WhenAny(changing, Task.Delay(ReadFor, Cancellation));
        Assert.False(changing.IsCompleted, changing.Exception?.ToString() ?? "The change didn't wait for the read.");

        await reading.CommitAsync(Cancellation);
        await changing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
    }
}
