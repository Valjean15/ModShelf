using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Sources.Abstractions.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// How the scheduler drives one job: resolving, transferring, verifying, and the ways that can end.
/// </summary>
public sealed class DownloadSchedulerTests
{
    [Fact]
    public async Task DownloadsAQueuedJobToCompletion()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        TransferCall call = await harness.Transfer.NextCallAsync();
        call.Succeed(totalBytes: 100);

        DownloadJobSnapshot done = await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal(100, done.BytesDownloaded);
        Assert.Equal(Path.Combine(TestData.TargetDirectory, "35407.7z"), call.DestinationPath);

        DownloadJobStatus[] expected = [Queued, Resolving, Downloading, Verifying, Completed];
        Assert.Equal(expected, harness.Statuses(job.Id));
        Assert.Equal(Completed, harness.Repository.SavedStatuses(job.Id)[^1]);
    }

    [Fact]
    public async Task WaitsForAHandoffWhenTheSourceAsksForOne()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new HandoffRequired(new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604")));

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        await harness.WaitForStatusAsync(job.Id, AwaitingHandoff);
        Assert.Equal(0, harness.Transfer.CallCount);
    }

    [Fact]
    public async Task ResolvesAFreshTicketWhenTheLinkExpiresAndContinues()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        TransferCall first = await harness.Transfer.NextCallAsync();
        first.Progress.Started(0, 100);
        first.Progress.Advanced(40);
        first.End(TransferOutcome.LinkExpired);

        TransferCall second = await harness.Transfer.NextCallAsync();
        Assert.Equal(40, (await harness.CurrentAsync(job.Id)).BytesDownloaded);
        second.Progress.Started(40, 100);
        second.Progress.Advanced(100);
        second.Progress.Verifying();
        second.End(TransferOutcome.Completed);

        await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal(2, harness.Nexus.Calls);
        DownloadJobStatus[] expected = [Queued, Resolving, Downloading, Resolving, Downloading, Verifying, Completed];
        Assert.Equal(expected, harness.Statuses(job.Id));
    }

    [Fact]
    public async Task FailsAfterTooManyExpiredLinks()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(new DownloadSchedulerOptions { MaxLinkRefreshes = 1 });
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        (await harness.Transfer.NextCallAsync()).End(TransferOutcome.LinkExpired);
        (await harness.Transfer.NextCallAsync()).End(TransferOutcome.LinkExpired);

        DownloadJobSnapshot failed = await harness.WaitForStatusAsync(job.Id, Failed);
        Assert.Equal(DownloadFailureReason.LinkUnavailable, failed.FailureReason);
        Assert.Equal(2, harness.Transfer.CallCount);
    }

    [Theory]
    [InlineData(TransferOutcome.HashMismatch, DownloadFailureReason.HashMismatch)]
    [InlineData(TransferOutcome.NetworkError, DownloadFailureReason.Network)]
    [InlineData(TransferOutcome.StorageError, DownloadFailureReason.Storage)]
    public async Task TurnsTransferProblemsIntoFailureReasons(TransferOutcome outcome, DownloadFailureReason reason)
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        (await harness.Transfer.NextCallAsync()).End(outcome);

        DownloadJobSnapshot failed = await harness.WaitForStatusAsync(job.Id, Failed);
        Assert.Equal(reason, failed.FailureReason);
    }

    [Fact]
    public async Task FailsOnAnUnexpectedErrorAndLogsItWithoutTheUrl()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        (await harness.Transfer.NextCallAsync()).Throw(new IOException("The disk caught fire."));

        DownloadJobSnapshot failed = await harness.WaitForStatusAsync(job.Id, Failed);
        Assert.Equal(DownloadFailureReason.Unexpected, failed.FailureReason);
        Assert.Contains(harness.Logger.Entries, entry => entry.Contains("The disk caught fire.", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Contains(ScriptedResolver.UrlSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsWhenTheSourceThrows()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenThrow(new HttpRequestException("Service unavailable."));

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        DownloadJobSnapshot failed = await harness.WaitForStatusAsync(job.Id, Failed);
        Assert.Equal(DownloadFailureReason.Unexpected, failed.FailureReason);
        Assert.Equal(0, harness.Transfer.CallCount);
    }

    [Fact]
    public async Task FailsAJobWhoseSourceIsNotRegistered()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(registerModIo: false);

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.ModIo, "77");

        DownloadJobSnapshot failed = await harness.WaitForStatusAsync(job.Id, Failed);
        Assert.Equal(DownloadFailureReason.LinkUnavailable, failed.FailureReason);
        Assert.Equal(0, harness.Transfer.CallCount);
        Assert.Contains(harness.Logger.Entries, entry => entry.Contains("mod-io", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnInvalidJobBeforeQueueingIt()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Scheduler.EnqueueAsync(
            TestData.File(TestData.Nexus, "35407"), "Downloads", "35407.7z", TestContext.Current.CancellationToken));

        Assert.Empty(await harness.SettleAsync());
    }

    [Fact]
    public async Task PublishesProgressAtMostOncePerInterval()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        TransferCall call = await harness.Transfer.NextCallAsync();

        call.Progress.Started(0, 1000);
        call.Progress.Advanced(100);
        call.Progress.Advanced(200);

        // The scheduler reads the clock when it handles a report, so let it catch up before moving the clock.
        await harness.SettleAsync();
        harness.Time.Advance(new DownloadSchedulerOptions().ProgressInterval);
        call.Progress.Advanced(300);
        call.Progress.Advanced(400);
        call.Progress.Verifying();

        // Snapshots arrive in order, so once Verifying is recorded, every earlier one is too.
        await harness.WaitForStatusAsync(job.Id, Verifying);
        long[] published = [0, 100, 300];
        Assert.Equal(published, harness.ProgressSnapshots(job.Id));
    }
}
