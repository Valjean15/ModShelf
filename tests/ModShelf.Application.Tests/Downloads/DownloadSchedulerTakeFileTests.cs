using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// Jobs waiting on a site's page that take the file the user's browser downloaded there instead (ADR 0091).
/// </summary>
public sealed class DownloadSchedulerTakeFileTests
{
    private const string Sha256 = "4e6ec40ee934696a7de4a03e2a043380a68e1b2bfd1ec9134886d0745b86e4ea";
    private const string Md5 = "0123456789abcdef0123456789abcdef";

    private static readonly ModFileRef File = TestData.File(TestData.Nexus, "35407");
    private static readonly Uri Page = new("https://www.mediafire.com/file/abc/35407.7z");
    private static readonly string Downloaded = Path.Combine(Path.GetTempPath(), "Browser", "35407.7z");
    private static readonly string Destination = Path.Combine(TestData.TargetDirectory, "35407.7z");

    [Fact]
    public async Task AJobWaitingOnItsPageTakesTheFileAndCompletes()
    {
        await using SchedulerHarness harness = await WaitingAsync();
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        TakeFileOutcome outcome = await harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);

        Assert.Equal(TakeFileOutcome.Taken, outcome);
        Assert.Null((await harness.WaitForStatusAsync(job.Id, Completed)).HandoffPage);
        Assert.Equal([(Downloaded, (FileHash?)null, Destination)], harness.Transfer.Taken);
        DownloadJobStatus[] statuses = [Queued, Resolving, AwaitingHandoff, Verifying, Completed];
        Assert.Equal(statuses, harness.Statuses(job.Id));
        Assert.Equal(Completed, harness.Repository.SavedStatuses(job.Id)[^1]);
        Assert.Contains(harness.Logger.Entries, entry => entry.Contains("took the file the browser downloaded", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Contains(Downloaded, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true, FileHashAlgorithm.Sha256)]
    [InlineData(false, true, FileHashAlgorithm.Md5)]
    [InlineData(true, false, FileHashAlgorithm.Sha256)]
    public async Task ChecksTheFileWithTheSitesSha256OrElseItsOtherHash(bool sha256, bool md5, FileHashAlgorithm checkedWith)
    {
        await using SchedulerHarness harness = await WaitingAsync(harness =>
        {
            List<FileHash> hashes = [];
            if (md5)
            {
                hashes.Add(new FileHash(FileHashAlgorithm.Md5, Md5));
            }

            if (sha256)
            {
                hashes.Add(new FileHash(FileHashAlgorithm.Sha256, Sha256));
            }

            // Another file of the mod first: its hash isn't this one's.
            harness.NexusSite.Files.Add(FileOf(TestData.File(TestData.Nexus, "99999"), [new FileHash(FileHashAlgorithm.Sha256, new string('a', 64))]));
            harness.NexusSite.Files.Add(FileOf(File, hashes));
        });
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        await harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);

        FileHash? checkedHash = Assert.Single(harness.Transfer.Taken).Hash;
        Assert.Equal((checkedWith, checkedWith == FileHashAlgorithm.Sha256 ? Sha256 : Md5), (checkedHash?.Algorithm, checkedHash?.Value));
    }

    [Fact]
    public async Task TakesTheFileUncheckedWhenTheSiteCantSayItsHash()
    {
        await using SchedulerHarness harness = await WaitingAsync(harness =>
        {
            harness.NexusSite.Files.Add(FileOf(File, [new FileHash(FileHashAlgorithm.Sha256, Sha256)]));
            harness.NexusSite.Unreachable = true;
        });
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        TakeFileOutcome outcome = await harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);

        Assert.Equal(TakeFileOutcome.Taken, outcome);
        Assert.Null(Assert.Single(harness.Transfer.Taken).Hash);
    }

    [Theory]
    [InlineData(TransferOutcome.HashMismatch, TakeFileOutcome.HashMismatch)]
    [InlineData(TransferOutcome.StorageError, TakeFileOutcome.StorageError)]
    [InlineData(TransferOutcome.NetworkError, TakeFileOutcome.StorageError)]
    public async Task AFileNotTakenLeavesTheJobWaitingOnItsPage(TransferOutcome transfer, TakeFileOutcome expected)
    {
        await using SchedulerHarness harness = await WaitingAsync();
        harness.Transfer.TakeOutcome = _ => Task.FromResult(transfer);
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        TakeFileOutcome outcome = await harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome);
        DownloadJobSnapshot now = await harness.CurrentAsync(job.Id);
        Assert.Equal((AwaitingHandoff, Page), (now.Status, now.HandoffPage));
        DownloadJobStatus[] statuses = [Queued, Resolving, AwaitingHandoff, Verifying, AwaitingHandoff];
        Assert.Equal(statuses, harness.Statuses(job.Id));
        Assert.Contains(harness.Logger.Entries, entry => entry.Contains($"didn't take the file it was handed ({expected})", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), TakeFileOutcome.NotFound)]
    [InlineData(typeof(IOException), TakeFileOutcome.StorageError)]
    [InlineData(typeof(UnauthorizedAccessException), TakeFileOutcome.StorageError)]
    public async Task AFileThatCantBeReadIsntTaken(Type failure, TakeFileOutcome expected)
    {
        await using SchedulerHarness harness = await WaitingAsync();
        harness.Transfer.TakeOutcome = _ => Task.FromException<TransferOutcome>((Exception)Activator.CreateInstance(failure)!);
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        TakeFileOutcome outcome = await harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome);
        Assert.Equal(AwaitingHandoff, (await harness.CurrentAsync(job.Id)).Status);
    }

    [Fact]
    public async Task OnlyAJobWaitingOnItsPageTakesAFile()
    {
        await using SchedulerHarness harness = await SchedulerHarness.StartAsync();
        harness.Nexus.ThenReturn(new DownloadTicket(new Uri("https://files.example/35407.7z")));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        TransferCall downloading = await harness.Transfer.NextCallAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => harness.Scheduler.TakeFileAsync(new DownloadJobId(Guid.NewGuid()), Downloaded, TestContext.Current.CancellationToken));

        Assert.Contains("only one waiting on a site's page", refused.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Transfer.Taken);
        downloading.Succeed();
        await harness.WaitForStatusAsync(job.Id, Completed);
    }

    [Theory]
    [InlineData("35407.7z")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RefusesAPathThatIsntFull(string path)
    {
        await using SchedulerHarness harness = await WaitingAsync();
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.Scheduler.TakeFileAsync(job.Id, path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AJobCancelledWhileItsFileIsTakenStaysCancelled()
    {
        await using SchedulerHarness harness = await WaitingAsync();
        TaskCompletionSource<TransferOutcome> moved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Transfer.TakeOutcome = _ => moved.Task;
        DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());
        Task<TakeFileOutcome> taking = harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);
        await harness.WaitForStatusAsync(job.Id, Verifying);

        await harness.Scheduler.CancelAsync(job.Id, TestContext.Current.CancellationToken);

        // The take was cancelled with it.
        Assert.Equal(TakeFileOutcome.StorageError, await taking.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(Cancelled, (await harness.CurrentAsync(job.Id)).Status);
        Assert.Equal(Cancelled, harness.Statuses(job.Id)[^1]);
    }

    [Fact]
    public async Task StoppingWhileAFileIsTakenLeavesTheJobWaitingOnItsPage()
    {
        SchedulerHarness harness = await WaitingAsync();
        await using (harness)
        {
            harness.Transfer.TakeOutcome = _ => new TaskCompletionSource<TransferOutcome>().Task;
            DownloadJobSnapshot job = Assert.Single(await harness.SettleAsync());
            Task<TakeFileOutcome> taking = harness.Scheduler.TakeFileAsync(job.Id, Downloaded, TestContext.Current.CancellationToken);
            await harness.WaitForStatusAsync(job.Id, Verifying);

            await harness.StopAsync();

            Assert.Equal(TakeFileOutcome.StorageError, await taking.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(AwaitingHandoff, harness.Repository.SavedStatuses(job.Id)[^1]);
        }
    }

    /// <summary>
    /// A harness whose one job waits on <see cref="Page"/>.
    /// </summary>
    private static async Task<SchedulerHarness> WaitingAsync(Action<SchedulerHarness>? beforeStart = null)
    {
        SchedulerHarness harness = await SchedulerHarness.StartAsync(beforeStart: beforeStart);
        harness.Nexus.ThenReturn(new HandoffRequired(Page));
        DownloadJobSnapshot job = await harness.EnqueueAsync(TestData.Nexus, "35407");
        await harness.WaitForStatusAsync(job.Id, AwaitingHandoff);
        return harness;
    }

    private static ModFile FileOf(ModFileRef file, IEnumerable<FileHash> hashes)
        => new(file, file.Id, $"{file.Id}.7z", "1.0", ModFileCategory.Main, 1000, TestData.Now, hashes);
}
