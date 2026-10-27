using Microsoft.EntityFrameworkCore;

using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

public sealed class DownloadJobRepositoryTests : IAsyncLifetime
{
    private static readonly ModFileRef SkyUiMainFile = new(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407");

    // Sub-millisecond ticks and a non-UTC offset, to prove the time comes back exactly.
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 10, 23, 9, 30, 15, TimeSpan.FromHours(-6)).AddTicks(1234567);

    private readonly TestLibraryDatabase _database = new();

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AnEmptyLibraryHasNoJobs()
    {
        Assert.Empty(await _database.Repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AJobSurvivesARestartWithEveryValue()
    {
        DownloadJob job = NewJob("SkyUI 5.2 SE (Ünïcode).7z");
        job.StartResolving();
        job.StartDownloading(resumeFromByte: 0, totalBytes: 1_073_741_824);
        job.ReportProgress(536_870_912);
        await _database.Repository.AddAsync(job, TestContext.Current.CancellationToken);

        await _database.RestartAsync();
        DownloadJob stored = Assert.Single(await _database.Repository.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(job.Id, stored.Id);
        Assert.Equal(job.File, stored.File);
        Assert.Equal(job.TargetDirectory, stored.TargetDirectory);
        Assert.Equal(job.FileName, stored.FileName);
        Assert.Equal(job.CreatedAt, stored.CreatedAt);
        Assert.Equal(job.CreatedAt.Offset, stored.CreatedAt.Offset);
        Assert.Equal(DownloadJobStatus.Downloading, stored.Status);
        Assert.Equal(536_870_912, stored.BytesDownloaded);
        Assert.Equal(1_073_741_824, stored.TotalBytes);
        Assert.Null(stored.FailureReason);
    }

    [Fact]
    public async Task ListsJobsInTheOrderTheyWereAdded()
    {
        // Created in the same instant, as with a coarse clock or a batch of files.
        DownloadJob[] jobs = [NewJob("A.7z"), NewJob("B.7z"), NewJob("C.7z")];
        await _database.Repository.AddAsync(jobs[1], TestContext.Current.CancellationToken);
        await _database.Repository.AddAsync(jobs[0], TestContext.Current.CancellationToken);
        await _database.Repository.AddAsync(jobs[2], TestContext.Current.CancellationToken);

        await _database.RestartAsync();
        IReadOnlyList<DownloadJob> stored = await _database.Repository.ListAsync(TestContext.Current.CancellationToken);

        DownloadJobId[] expected = [jobs[1].Id, jobs[0].Id, jobs[2].Id];
        Assert.Equal(expected, stored.Select(job => job.Id));
    }

    [Fact]
    public async Task DeletesTheJobsAskedFor()
    {
        DownloadJob[] jobs = [NewJob("A.7z"), NewJob("B.7z"), NewJob("C.7z")];
        foreach (DownloadJob job in jobs)
        {
            await _database.Repository.AddAsync(job, TestContext.Current.CancellationToken);
        }

        await _database.Repository.DeleteAsync([jobs[0].Id, jobs[2].Id, DownloadJobId.New()], TestContext.Current.CancellationToken);
        await _database.Repository.DeleteAsync([], TestContext.Current.CancellationToken);

        await _database.RestartAsync();
        Assert.Equal(jobs[1].Id, Assert.Single(await _database.Repository.ListAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task UpdateSavesTheStatusProgressAndReason()
    {
        DownloadJob job = NewJob();
        await _database.Repository.AddAsync(job, TestContext.Current.CancellationToken);

        job.StartResolving();
        job.StartDownloading(resumeFromByte: 0, totalBytes: 1000);
        job.ReportProgress(400);
        await _database.Repository.UpdateAsync(job, TestContext.Current.CancellationToken);
        DownloadJob downloading = await StoredAsync(job.Id);
        Assert.Equal(DownloadJobStatus.Downloading, downloading.Status);
        Assert.Equal(400, downloading.BytesDownloaded);
        Assert.Equal(1000, downloading.TotalBytes);

        job.Fail(DownloadFailureReason.Network);
        await _database.Repository.UpdateAsync(job, TestContext.Current.CancellationToken);
        DownloadJob failed = await StoredAsync(job.Id);
        Assert.Equal(DownloadJobStatus.Failed, failed.Status);
        Assert.Equal(DownloadFailureReason.Network, failed.FailureReason);

        job.Retry();
        await _database.Repository.UpdateAsync(job, TestContext.Current.CancellationToken);
        DownloadJob retried = await StoredAsync(job.Id);
        Assert.Equal(DownloadJobStatus.Queued, retried.Status);
        Assert.Null(retried.FailureReason);
        Assert.Equal(400, retried.BytesDownloaded);
    }

    [Fact]
    public async Task UpdateLeavesOtherJobsAlone()
    {
        DownloadJob updated = NewJob("A.7z");
        DownloadJob untouched = NewJob("B.7z");
        await _database.Repository.AddAsync(updated, TestContext.Current.CancellationToken);
        await _database.Repository.AddAsync(untouched, TestContext.Current.CancellationToken);

        updated.Pause();
        await _database.Repository.UpdateAsync(updated, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadJobStatus.Paused, (await StoredAsync(updated.Id)).Status);
        Assert.Equal(DownloadJobStatus.Queued, (await StoredAsync(untouched.Id)).Status);
    }

    [Fact]
    public async Task UpdatingAJobThatWasNeverAddedThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _database.Repository.UpdateAsync(NewJob(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddingTheSameJobTwiceThrows()
    {
        DownloadJob job = NewJob();
        await _database.Repository.AddAsync(job, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _database.Repository.AddAsync(job, TestContext.Current.CancellationToken));
        Assert.Single(await _database.Repository.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListNamesARowThatHoldsAnImpossibleJob()
    {
        DownloadJob job = NewJob();
        await _database.Repository.AddAsync(job, TestContext.Current.CancellationToken);
        await _database.QueryAsync("UPDATE DownloadJobs SET Status = 99");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _database.Repository.ListAsync(TestContext.Current.CancellationToken));

        Assert.Contains(job.Id.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private DownloadJob NewJob(string fileName = "SkyUI_5_2_SE.7z")
        => DownloadJob.Create(SkyUiMainFile, Path.Combine(Path.GetDirectoryName(_database.DatabasePath)!, "My Mods"), fileName, CreatedAt);

    private async Task<DownloadJob> StoredAsync(DownloadJobId id)
        => (await _database.Repository.ListAsync(TestContext.Current.CancellationToken)).Single(job => job.Id == id);
}
