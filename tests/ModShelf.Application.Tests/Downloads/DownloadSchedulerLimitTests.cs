using ModShelf.Application.Downloads;
using ModShelf.Application.Tests.Fakes;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// How many jobs run at once, and in which order they start.
/// </summary>
public sealed class DownloadSchedulerLimitTests
{
    [Fact]
    public async Task RunsNoMoreJobsThanTheGlobalLimit()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            new DownloadSchedulerOptions { MaxConcurrentDownloads = 2, MaxConcurrentDownloadsPerSource = 5 });

        await harness.EnqueueAsync(TestData.Nexus, "a");
        await harness.EnqueueAsync(TestData.ModIo, "b");
        DownloadJobSnapshot third = await harness.EnqueueAsync(TestData.Nexus, "c");
        TransferCall first = await harness.Transfer.NextCallAsync();
        await harness.Transfer.NextCallAsync();

        Assert.Equal(Queued, (await harness.CurrentAsync(third.Id)).Status);
        Assert.Equal(2, harness.Transfer.CallCount);

        first.Succeed();
        TransferCall next = await harness.Transfer.NextCallAsync();
        Assert.Equal(Path.Combine(TestData.TargetDirectory, "c.7z"), next.DestinationPath);
    }

    [Fact]
    public async Task RunsNoMoreJobsPerSourceThanThePerSourceLimit()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            new DownloadSchedulerOptions { MaxConcurrentDownloads = 5, MaxConcurrentDownloadsPerSource = 1 });

        await harness.EnqueueAsync(TestData.Nexus, "a");
        DownloadJobSnapshot secondNexus = await harness.EnqueueAsync(TestData.Nexus, "b");
        await harness.EnqueueAsync(TestData.ModIo, "c");
        TransferCall[] running = [await harness.Transfer.NextCallAsync(), await harness.Transfer.NextCallAsync()];

        string[] expected = ["a.7z", "c.7z"];
        Assert.Equal(expected, running.Select(call => Path.GetFileName(call.DestinationPath)).Order(StringComparer.Ordinal));
        Assert.Equal(Queued, (await harness.CurrentAsync(secondNexus.Id)).Status);

        Assert.Single(running, call => call.DestinationPath.EndsWith("a.7z", StringComparison.Ordinal)).Succeed();
        TransferCall next = await harness.Transfer.NextCallAsync();
        Assert.Equal(Path.Combine(TestData.TargetDirectory, "b.7z"), next.DestinationPath);
    }

    [Fact]
    public async Task StartsJobsInTheOrderTheyWereQueued()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync(
            new DownloadSchedulerOptions { MaxConcurrentDownloads = 1 });

        await harness.EnqueueAsync(TestData.Nexus, "a");
        await harness.EnqueueAsync(TestData.ModIo, "b");
        await harness.EnqueueAsync(TestData.Nexus, "c");

        List<string> started = [];
        for (int i = 0; i < 3; i++)
        {
            TransferCall call = await harness.Transfer.NextCallAsync();
            started.Add(Path.GetFileName(call.DestinationPath));
            call.Succeed();
        }

        string[] expected = ["a.7z", "b.7z", "c.7z"];
        Assert.Equal(expected, started);
    }
}
