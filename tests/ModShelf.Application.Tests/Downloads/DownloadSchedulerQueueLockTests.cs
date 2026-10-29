using System.Diagnostics;

using ModShelf.Application.Downloads;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// One process at a time runs the queue: the one holding the queue lock. Another waits for it, without touching the
/// jobs, as when the CLI downloads while the desktop app runs.
/// </summary>
public sealed class DownloadSchedulerQueueLockTests
{
    [Fact]
    public async Task TakesTheLockWhenItStartsAndLetsItGoWhenItStops()
    {
        SchedulerHarness harness = await SchedulerHarness.StartAsync();
        await using (harness)
        {
            bool heldWhileRunning = harness.QueueLock.Held;

            await harness.StopAsync();

            Assert.True(heldWhileRunning);
            Assert.False(harness.Scheduler.IsQueueBusyElsewhere);
            Assert.True(harness.Scheduler.WhenQueueRunsHereAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
            Assert.False(harness.QueueLock.Held);
        }
    }

    [Fact]
    public async Task WhileAnotherProcessRunsTheQueueTheSavedJobsAreListedAndLeftAsTheyAre()
    {
        DownloadJob running = StoredJob("1");
        running.StartResolving();
        running.StartDownloading(40, 100);

        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(storedJobs: [running, StoredJob("2")], queueBusyElsewhere: true);

        IReadOnlyList<DownloadJobSnapshot> listed = await harness.SettleAsync();
        Assert.True(harness.Scheduler.IsQueueBusyElsewhere);
        Assert.Equal([Downloading, Queued], listed.Select(job => job.Status));
        Assert.Empty(harness.Repository.SavedStatuses(running.Id));
        Assert.Equal(0, harness.Transfer.CallCount);
        Assert.Contains(harness.Logger.Entries, entry => entry.StartsWith("Information: Another ModShelf process runs the downloads", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhileAnotherProcessRunsTheQueueNothingCanBeAddedOrChanged()
    {
        DownloadJob stored = StoredJob("1");
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(storedJobs: [stored], queueBusyElsewhere: true);
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<DownloadQueueBusyException>(() => harness.EnqueueAsync(TestData.Nexus, "2"));
        await Assert.ThrowsAsync<DownloadQueueBusyException>(() => harness.Scheduler.PauseAsync(stored.Id, cancellation));
        await Assert.ThrowsAsync<DownloadQueueBusyException>(() => harness.Scheduler.CancelAsync(stored.Id, cancellation));
        await Assert.ThrowsAsync<DownloadQueueBusyException>(() => harness.Scheduler.ClearFinishedAsync(cancellation));
        await Assert.ThrowsAsync<DownloadQueueBusyException>(() => harness.Scheduler.HandOffAsync(stored.File, new Uri("nxm://game/mods/1/files/1"), cancellation));
        Assert.Single(await harness.SettleAsync());
    }

    [Fact]
    public async Task RunsTheQueueOnceTheOtherProcessLetsGo()
    {
        DownloadJob interrupted = StoredJob("1");
        interrupted.StartResolving();
        interrupted.StartDownloading(40, 100);
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(storedJobs: [interrupted], queueBusyElsewhere: true);
        Task runsHere = harness.Scheduler.WhenQueueRunsHereAsync(TestContext.Current.CancellationToken);
        bool ranBefore = runsHere.IsCompleted;

        harness.QueueLock.HeldElsewhere = false;
        await AdvanceUntilAsync(harness, () => runsHere.IsCompleted);

        TransferCall call = await harness.Transfer.NextCallAsync();
        Assert.False(ranBefore);
        Assert.False(harness.Scheduler.IsQueueBusyElsewhere);
        Assert.Equal(Path.Combine(TestData.TargetDirectory, "1.7z"), call.DestinationPath);
        Assert.Equal([Queued, Resolving], harness.Repository.SavedStatuses(interrupted.Id));
    }

    [Fact]
    public async Task KeepsTryingEveryRetryIntervalWhileTheOtherProcessHoldsIt()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(queueBusyElsewhere: true);
        await harness.SettleAsync();
        int before = harness.QueueLock.Tries;

        await AdvanceUntilAsync(harness, () => harness.QueueLock.Tries >= before + 3);

        Assert.True(harness.Scheduler.IsQueueBusyElsewhere);
        Assert.False(harness.QueueLock.Held);
    }

    [Fact]
    public async Task StoppingWhileWaitingStopsAtOnce()
    {
        SchedulerHarness harness = await SchedulerHarness.StartAsync(queueBusyElsewhere: true);
        await using (harness)
        {
            await harness.StopAsync();

            Assert.False(harness.QueueLock.Held);
            await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Scheduler.ListAsync(TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// Moves the fake clock on, a retry interval at a time, until <paramref name="done"/>.
    /// </summary>
    private static async Task AdvanceUntilAsync(SchedulerHarness harness, Func<bool> done)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(waited.Elapsed < SchedulerTimeouts.Wait, "The scheduler didn't take the lock in time.");
            harness.Time.Advance(new DownloadSchedulerOptions().QueueLockRetryInterval);
            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }
    }

    private static DownloadJob StoredJob(string fileId)
        => DownloadJob.Create(TestData.File(TestData.Nexus, fileId), TestData.TargetDirectory, $"{fileId}.7z", TestData.Now);
}
