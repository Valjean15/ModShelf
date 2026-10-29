using ModShelf.Application.Downloads;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// The scheduler when the database can't save a job, such as when it's locked or the disk is full: downloads go on, and
/// the job is saved once the database works again.
/// </summary>
public sealed class DownloadSchedulerDatabaseTests
{
    private static readonly InvalidOperationException Locked = new("database is locked");

    [Fact]
    public async Task AQueueThatCantBeReadAtFirstIsReadAgainLater()
    {
        // Such as a database another program holds past SQLite's wait: the downloads start once it can be read,
        // rather than the scheduler, and the app with it, stopping.
        DownloadJob stored = DownloadJob.Create(TestData.File(TestData.Nexus, "35407"), TestData.TargetDirectory, "35407.7z", TestData.Now);
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            storedJobs: [stored],
            beforeStart: harness => harness.Repository.ListFailures.Enqueue(Locked));

        Task<IReadOnlyList<DownloadJobSnapshot>> listed = harness.Scheduler.ListAsync(CancellationToken.None);
        for (int step = 0; step < 200 && !listed.IsCompleted; step++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(stored.Id, Assert.Single(await listed.WaitAsync(SchedulerTimeouts.Wait, TestContext.Current.CancellationToken)).Id);
        Assert.Contains(harness.Logger.Entries, entry => entry.StartsWith("Warning: Couldn't read the download queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AJobWhoseStartCantBeSavedStillDownloads()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Repository.UpdateFailures.Enqueue(Locked);

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        (await harness.Transfer.NextCallAsync()).Succeed(totalBytes: 100);

        await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal(Completed, harness.Repository.SavedStatuses(job.Id)[^1]);
        Assert.Contains(harness.Logger.Entries, entry => entry.StartsWith($"Warning: Couldn't save download job {job.Id.Value}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSchedulerKeepsTakingJobsAfterASaveFailed()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Repository.UpdateFailures.Enqueue(Locked);
        await harness.EnqueueAsync(TestData.Nexus, "1");

        DownloadJobSnapshot second = await harness.EnqueueAsync(TestData.ModIo, "2");

        Assert.Equal(2, (await harness.SettleAsync()).Count);
        Assert.Equal(Queued, second.Status);
    }

    [Fact]
    public async Task AFinishedDownloadWhoseSaveFailedIsStillToldAndSavedLater()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot first = await harness.EnqueueAsync(TestData.Nexus, "1");
        TransferCall call = await harness.Transfer.NextCallAsync();
        call.Progress.Started(0, 100);
        call.Progress.Advanced(100);
        call.Progress.Verifying();
        await harness.WaitForStatusAsync(first.Id, Verifying);

        harness.Repository.UpdateFailures.Enqueue(Locked);
        call.End(TransferOutcome.Completed);
        await harness.WaitForStatusAsync(first.Id, Completed);
        IReadOnlyList<DownloadJobStatus> savedBefore = harness.Repository.SavedStatuses(first.Id);

        // Any save that works, such as the next job's, saves what couldn't be saved before.
        await harness.EnqueueAsync(TestData.ModIo, "2");
        await harness.SettleAsync();

        Assert.NotEqual(Completed, savedBefore[^1]);
        Assert.Equal(Completed, harness.Repository.SavedStatuses(first.Id)[^1]);
    }
}
