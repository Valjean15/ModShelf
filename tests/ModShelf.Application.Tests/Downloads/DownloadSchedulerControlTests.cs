using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// Pause, resume, retry and cancel, on running jobs and on waiting ones.
/// </summary>
public sealed class DownloadSchedulerControlTests
{
    [Fact]
    public async Task PausingARunningDownloadStopsItAndKeepsTheProgress()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, TransferCall call) = await StartDownloadAsync(harness, bytesDownloaded: 40);

        await harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken);

        DownloadJobSnapshot paused = await harness.CurrentAsync(job.Id);
        Assert.Equal(Paused, paused.Status);
        Assert.Equal(40, paused.BytesDownloaded);
        Assert.True(call.CancellationToken.IsCancellationRequested);
        Assert.Empty(harness.Transfer.DeletedPartialFiles);
    }

    [Fact]
    public async Task ResumingAPausedDownloadStartsItAgainWithAFreshTicket()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, TransferCall first) = await StartDownloadAsync(harness, bytesDownloaded: 40);
        await harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken);

        await harness.Scheduler.ResumeAsync(job.Id, TestContext.Current.CancellationToken);

        TransferCall second = await harness.Transfer.NextCallAsync();
        Assert.Equal(first.DestinationPath, second.DestinationPath);
        Assert.Equal(2, harness.Nexus.Calls);
    }

    [Fact]
    public async Task PausingWhileVerifyingStillPauses()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, TransferCall call) = await StartDownloadAsync(harness, bytesDownloaded: 100);
        call.Progress.Verifying();
        await harness.WaitForStatusAsync(job.Id, Verifying);

        await harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Paused, (await harness.CurrentAsync(job.Id)).Status);
    }

    [Fact]
    public async Task CancellingARunningDownloadDeletesItsPartialFile()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, TransferCall call) = await StartDownloadAsync(harness, bytesDownloaded: 40);

        await harness.Scheduler.CancelAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Cancelled, (await harness.CurrentAsync(job.Id)).Status);
        Assert.Equal(call.DestinationPath, Assert.Single(harness.Transfer.DeletedPartialFiles));
    }

    [Fact]
    public async Task CancellingARunningDownloadSaysWhenThePartialFileCantBeDeleted()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, _) = await StartDownloadAsync(harness, bytesDownloaded: 40);
        harness.Transfer.DeleteFailure = new IOException("Another program has the file open.");

        IOException failed = await Assert.ThrowsAsync<IOException>(
            () => harness.Scheduler.CancelAsync(job.Id, TestContext.Current.CancellationToken).WaitAsync(SchedulerTimeouts.Wait, TestContext.Current.CancellationToken));

        Assert.Same(harness.Transfer.DeleteFailure, failed);
        Assert.Equal(Cancelled, (await harness.CurrentAsync(job.Id)).Status);
    }

    [Fact]
    public async Task CancellingAPausedDownloadDeletesItsPartialFile()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot job, TransferCall call) = await StartDownloadAsync(harness, bytesDownloaded: 40);
        await harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken);

        await harness.Scheduler.CancelAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Cancelled, (await harness.CurrentAsync(job.Id)).Status);
        Assert.Equal(call.DestinationPath, Assert.Single(harness.Transfer.DeletedPartialFiles));
    }

    [Fact]
    public async Task PausingAQueuedJobKeepsItFromStarting()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            new DownloadSchedulerOptions { MaxConcurrentDownloads = 1 });
        await harness.EnqueueAsync(TestData.Nexus, "a");
        DownloadJobSnapshot waiting = await harness.EnqueueAsync(TestData.Nexus, "b");
        TransferCall running = await harness.Transfer.NextCallAsync();

        await harness.Scheduler.PauseAsync(waiting.Id, TestContext.Current.CancellationToken);
        running.Succeed();
        await harness.SettleAsync();

        Assert.Equal(Paused, (await harness.CurrentAsync(waiting.Id)).Status);
        Assert.Equal(1, harness.Transfer.CallCount);
    }

    [Fact]
    public async Task RetryingAFailedJobRunsItAgain()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        (await harness.Transfer.NextCallAsync()).End(TransferOutcome.NetworkError);
        await harness.WaitForStatusAsync(job.Id, Failed);

        await harness.Scheduler.RetryAsync(job.Id, TestContext.Current.CancellationToken);
        (await harness.Transfer.NextCallAsync()).Succeed();

        DownloadJobSnapshot done = await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Null(done.FailureReason);
    }

    [Fact]
    public async Task RequestsForAnUnknownJobThrow()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => harness.Scheduler.PauseAsync(DownloadJobId.New(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RequestsTheStatusDoesNotAllowThrow()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        (DownloadJobSnapshot running, TransferCall call) = await StartDownloadAsync(harness, bytesDownloaded: 40);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Scheduler.RetryAsync(running.Id, TestContext.Current.CancellationToken));

        call.Progress.Advanced(100);
        call.Progress.Verifying();
        call.End(TransferOutcome.Completed);
        await harness.WaitForStatusAsync(running.Id, Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Scheduler.ResumeAsync(running.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Queues a Nexus job and lets its transfer start and report <paramref name="bytesDownloaded"/> of 100 bytes.
    /// </summary>
    private static async Task<(DownloadJobSnapshot Job, TransferCall Call)> StartDownloadAsync(
        SchedulerHarness harness,
        long bytesDownloaded)
    {
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        TransferCall call = await harness.Transfer.NextCallAsync();
        call.Progress.Started(0, 100);
        call.Progress.Advanced(bytesDownloaded);
        await harness.WaitForAsync(job.Id, snapshot => snapshot.BytesDownloaded == bytesDownloaded);
        return (job, call);
    }
}
