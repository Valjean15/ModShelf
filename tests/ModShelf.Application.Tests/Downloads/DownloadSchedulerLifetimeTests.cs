using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Sources.Abstractions.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// What happens to jobs across starts and stops of the app.
/// </summary>
public sealed class DownloadSchedulerLifetimeTests
{
    [Fact]
    public async Task RequeuesAndRestartsJobsThatWereInterruptedMidDownload()
    {
        DownloadJob interrupted = StoredJob();
        interrupted.StartResolving();
        interrupted.StartDownloading(40, 100);

        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(storedJobs: [interrupted]);

        TransferCall call = await harness.Transfer.NextCallAsync();
        Assert.Equal(Path.Combine(TestData.TargetDirectory, "35407.7z"), call.DestinationPath);
        DownloadJobStatus[] expected = [Queued, Resolving];
        Assert.Equal(expected, harness.Repository.SavedStatuses(interrupted.Id));
        Assert.Contains(harness.Logger.Entries, entry => entry.Contains("Requeued 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksTheSourceAgainAboutJobsThatWereWaitingForAHandoff()
    {
        // The page to start the download on is only kept in memory, so it's gone after a restart.
        Uri page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");
        DownloadJob waiting = StoredJob();
        waiting.StartResolving();
        waiting.AwaitHandoff();

        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            storedJobs: [waiting],
            beforeStart: harness => harness.Nexus.ThenReturn(new HandoffRequired(page)));

        DownloadJobSnapshot asked = await harness.WaitForAsync(waiting.Id, snapshot => snapshot.HandoffPage is not null);
        Assert.Equal(AwaitingHandoff, asked.Status);
        Assert.Equal(page, asked.HandoffPage);
        Assert.Equal([new ResolveCall(waiting.File, Handoff: null)], harness.Nexus.Resolved);
        DownloadJobStatus[] saved = [Resolving, AwaitingHandoff];
        Assert.Equal(saved, harness.Repository.SavedStatuses(waiting.Id));
        Assert.Equal(0, harness.Transfer.CallCount);
    }

    [Fact]
    public async Task StoppingRequeuesRunningDownloads()
    {
        SchedulerHarness harness = await SchedulerHarness.StartAsync();
        await using (harness)
        {
            DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
            TransferCall call = await harness.Transfer.NextCallAsync();
            call.Progress.Started(0, 100);

            await harness.StopAsync();

            Assert.True(call.CancellationToken.IsCancellationRequested);
            Assert.Equal(Queued, harness.Repository.SavedStatuses(job.Id)[^1]);
        }
    }

    [Fact]
    public async Task RequestsAfterStoppingThrow()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();

        await harness.StopAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Scheduler.EnqueueAsync(
            TestData.File(TestData.Nexus, "35407"), TestData.TargetDirectory, "35407.7z", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnswersWaitingRequestsEvenWhenStoppedBeforeItEverRan()
    {
        using DownloadScheduler scheduler = new(
            new InMemoryDownloadJobRepository([]),
            new SourceRegistry([]),
            new ControlledFileTransfer(),
            TimeProvider.System,
            new DownloadSchedulerOptions(),
            NullLogger<DownloadScheduler>.Instance);

        // A host that is already shutting down: BackgroundService then never runs ExecuteAsync.
        using CancellationTokenSource shuttingDown = new();
        await shuttingDown.CancelAsync();
        await scheduler.StartAsync(shuttingDown.Token);
        Task<IReadOnlyList<DownloadJobSnapshot>> waiting = scheduler.ListAsync(TestContext.Current.CancellationToken);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => waiting);
    }

    private static DownloadJob StoredJob()
        => DownloadJob.Create(TestData.File(TestData.Nexus, "35407"), TestData.TargetDirectory, "35407.7z", TestData.Now);
}
