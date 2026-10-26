using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// Jobs that wait for the user to start the download on the source's website, and the handoff links that wake them.
/// </summary>
public sealed class DownloadSchedulerHandoffTests
{
    private const string Key = "nxm-key-5ec2e7";

    private static readonly ModFileRef File = TestData.File(TestData.Nexus, "35407");
    private static readonly Uri Page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407&nmm=1");
    private static readonly Uri PageAgain = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");
    private static readonly Uri Handoff = new($"nxm://skyrimspecialedition/mods/12604/files/35407?key={Key}&expires=1792000000&user_id=1");
    private static readonly Uri NewerHandoff = new("nxm://skyrimspecialedition/mods/12604/files/35407?key=newer-key&expires=1792000600&user_id=1");

    [Fact]
    public async Task ShowsThePageToStartTheDownloadOnWhileTheJobWaits()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new HandoffRequired(Page));

        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        Assert.Equal(Page, (await harness.WaitForStatusAsync(job.Id, AwaitingHandoff)).HandoffPage);
        Assert.Equal(Page, (await harness.CurrentAsync(job.Id)).HandoffPage);
        Assert.Null(job.HandoffPage);
    }

    [Fact]
    public async Task AHandedOverLinkWakesTheWaitingJob()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new HandoffRequired(Page));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, AwaitingHandoff);

        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);

        (await harness.Transfer.NextCallAsync()).Succeed();
        Assert.Null((await harness.WaitForStatusAsync(job.Id, Completed)).HandoffPage);
        ResolveCall[] calls = [new(File, Handoff: null), new(File, Handoff)];
        Assert.Equal(calls, harness.Nexus.Resolved);
        DownloadJobStatus[] statuses = [Queued, Resolving, AwaitingHandoff, Resolving, Downloading, Verifying, Completed];
        Assert.Equal(statuses, harness.Statuses(job.Id));
    }

    [Fact]
    public async Task ALinkHandedOverBeforeTheJobIsAddedIsUsedFromTheStart()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal([new ResolveCall(File, Handoff)], harness.Nexus.Resolved);
        Assert.DoesNotContain(AwaitingHandoff, harness.Statuses(job.Id));
    }

    [Fact]
    public async Task WaitsForANewLinkWhenTheSourceRefusesTheOneGiven()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new HandoffRequired(Page)).ThenReturn(new HandoffRequired(PageAgain));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, AwaitingHandoff);

        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);

        // Asking again with the refused link would get a ticket from the script, so the job would download.
        await harness.WaitForAsync(job.Id, snapshot => snapshot.HandoffPage == PageAgain);
        await harness.SettleAsync();
        DownloadJobStatus[] statuses = [Queued, Resolving, AwaitingHandoff, Resolving, AwaitingHandoff];
        Assert.Equal(statuses, harness.Statuses(job.Id));
        Assert.Equal(2, harness.Nexus.Calls);

        await harness.Scheduler.HandOffAsync(File, NewerHandoff, TestContext.Current.CancellationToken);

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal(new ResolveCall(File, NewerHandoff), harness.Nexus.Resolved[^1]);
    }

    [Fact]
    public async Task KeepsALinkHandedOverWhileTheSourceRefusedAnOlderOne()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        TaskCompletionSource refuse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Nexus.ThenReturnWhen(refuse.Task, new HandoffRequired(Page));
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, Resolving);

        await harness.Scheduler.HandOffAsync(File, NewerHandoff, TestContext.Current.CancellationToken);
        refuse.SetResult();

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        ResolveCall[] calls = [new(File, Handoff), new(File, NewerHandoff)];
        Assert.Equal(calls, harness.Nexus.Resolved);
        Assert.DoesNotContain(harness.Snapshots(job.Id), snapshot => snapshot.HandoffPage is not null);
    }

    [Fact]
    public async Task KeepsALinkHandedOverWhileTheSourceAskedForOne()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        TaskCompletionSource ask = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Nexus.ThenReturnWhen(ask.Task, new HandoffRequired(Page));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, Resolving);

        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        ask.SetResult();

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        ResolveCall[] calls = [new(File, Handoff: null), new(File, Handoff)];
        Assert.Equal(calls, harness.Nexus.Resolved);
        Assert.DoesNotContain(harness.Snapshots(job.Id), snapshot => snapshot.HandoffPage is not null);
    }

    [Theory]
    [InlineData(Paused)]
    [InlineData(Cancelled)]
    public async Task APauseOrCancelWhileTheSourceIsAskedAppliesWhenItWantsAHandoff(DownloadJobStatus wanted)
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        TaskCompletionSource ask = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Nexus.ThenReturnWhen(ask.Task, new HandoffRequired(Page));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, Resolving);

        Task stopping = wanted == Paused
            ? harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken)
            : harness.Scheduler.CancelAsync(job.Id, TestContext.Current.CancellationToken);
        ask.SetResult();
        await stopping;

        DownloadJobSnapshot stopped = await harness.CurrentAsync(job.Id);
        Assert.Equal(wanted, stopped.Status);
        Assert.Null(stopped.HandoffPage);
        Assert.Equal(wanted == Cancelled ? 1 : 0, harness.Transfer.DeletedPartialFiles.Count);
    }

    [Fact]
    public async Task ReusesTheLinkWhenTheTicketExpiresMidDownload()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");

        (await harness.Transfer.NextCallAsync()).End(TransferOutcome.LinkExpired);
        (await harness.Transfer.NextCallAsync()).Succeed();

        await harness.WaitForStatusAsync(job.Id, Completed);
        ResolveCall[] calls = [new(File, Handoff), new(File, Handoff)];
        Assert.Equal(calls, harness.Nexus.Resolved);
    }

    [Fact]
    public async Task ARetryAfterAFailureUsesTheLinkAgain()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        (await harness.Transfer.NextCallAsync()).End(TransferOutcome.NetworkError);
        await harness.WaitForStatusAsync(job.Id, Failed);

        await harness.Scheduler.RetryAsync(job.Id, TestContext.Current.CancellationToken);

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        ResolveCall[] calls = [new(File, Handoff), new(File, Handoff)];
        Assert.Equal(calls, harness.Nexus.Resolved);
    }

    [Fact]
    public async Task AWaitingJobThatWasPausedAsksAgainWhenResumed()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new HandoffRequired(Page));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, AwaitingHandoff);

        await harness.Scheduler.PauseAsync(job.Id, TestContext.Current.CancellationToken);
        DownloadJobSnapshot paused = await harness.CurrentAsync(job.Id);
        await harness.Scheduler.ResumeAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Paused, paused.Status);
        Assert.Null(paused.HandoffPage);
        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
        Assert.Equal(2, harness.Nexus.Calls);
    }

    [Fact]
    public async Task ForgetsTheLinkOnceTheFileIsDownloaded()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot first = await harness.EnqueueAsync(TestData.Nexus, "35407");
        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(first.Id, Completed);

        DownloadJobSnapshot copy = await harness.Scheduler.EnqueueAsync(
            File, TestData.TargetDirectory, "copy.7z", TestContext.Current.CancellationToken);

        (await harness.Transfer.NextCallAsync()).Succeed();
        await harness.WaitForStatusAsync(copy.Id, Completed);
        Assert.Equal(new ResolveCall(File, Handoff: null), harness.Nexus.Resolved[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgetsTheLinkOnceNoJobForTheFileIsLeft(bool anotherJobForTheFile)
    {
        // One slot, held by another file's download, so the jobs for the file wait in the queue.
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(new DownloadSchedulerOptions { MaxConcurrentDownloads = 1 });
        await harness.EnqueueAsync(TestData.Nexus, "busy");
        TransferCall busy = await harness.Transfer.NextCallAsync();
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot cancelled = await harness.EnqueueAsync(TestData.Nexus, "35407");
        if (anotherJobForTheFile)
        {
            await harness.Scheduler.EnqueueAsync(File, TestData.TargetDirectory, "copy.7z", TestContext.Current.CancellationToken);
        }

        await harness.Scheduler.CancelAsync(cancelled.Id, TestContext.Current.CancellationToken);
        DownloadJobSnapshot later = await harness.Scheduler.EnqueueAsync(
            File, TestData.TargetDirectory, "later.7z", TestContext.Current.CancellationToken);
        busy.Succeed();

        (await harness.Transfer.NextCallAsync()).Succeed();
        Uri? expected = anotherJobForTheFile ? Handoff : null;
        Assert.Equal(new ResolveCall(File, expected), harness.Nexus.Resolved[1]);
        if (!anotherJobForTheFile)
        {
            await harness.WaitForStatusAsync(later.Id, Completed);
        }
    }

    [Fact]
    public async Task AsksAboutAWaitingJobBeforeStartingQueuedOnes()
    {
        DownloadJob queued = DownloadJob.Create(TestData.File(TestData.Nexus, "queued"), TestData.TargetDirectory, "queued.7z", TestData.Now);
        DownloadJob waiting = DownloadJob.Create(File, TestData.TargetDirectory, "35407.7z", TestData.Now.AddMinutes(1));
        waiting.StartResolving();
        waiting.AwaitHandoff();

        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            new DownloadSchedulerOptions { MaxConcurrentDownloads = 1 },
            storedJobs: [queued, waiting],
            beforeStart: harness => harness.Nexus.ThenReturn(new HandoffRequired(Page)));

        await harness.Transfer.NextCallAsync();
        ModFileRef[] asked = [File, queued.File];
        Assert.Equal(asked, harness.Nexus.Resolved.Select(call => call.File));
    }

    [Fact]
    public async Task NeverLogsTheLink()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenThrow(new AuthenticationFailedException("The site refused the account."));
        await harness.Scheduler.HandOffAsync(File, Handoff, TestContext.Current.CancellationToken);
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, Failed);

        await harness.Scheduler.RetryAsync(job.Id, TestContext.Current.CancellationToken);
        (await harness.Transfer.NextCallAsync()).Throw(new IOException("The disk caught fire."));

        await harness.WaitForAsync(job.Id, snapshot => snapshot.FailureReason == DownloadFailureReason.Unexpected);
        Assert.NotEmpty(harness.Logger.Entries);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Scheduler.HandOffAsync(
            File, new Uri("mods/12604/files/35407", UriKind.Relative), TestContext.Current.CancellationToken));
    }
}
