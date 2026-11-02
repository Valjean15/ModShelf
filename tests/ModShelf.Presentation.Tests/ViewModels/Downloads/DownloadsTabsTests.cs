using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// The Downloads screen's summary, its tabs, and the downloads waiting for the user with Open the next page.
/// </summary>
public sealed class DownloadsTabsTests : IDisposable
{
    private const long MiB = 1024 * 1024;

    private readonly DownloadsHarness _harness = new();

    private DownloadsViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task SaysWhatsUnderWayThenWhatWaitsForTheUser()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot first = Downloading("a.7z", 16 * MiB);
        DownloadJobSnapshot second = Downloading("b.7z", 32 * MiB);
        await _harness.StartWithAsync(
            first,
            second,
            TestData.Job("resolving.7z", DownloadJobStatus.Resolving),
            TestData.Job("queued.7z"),
            TestData.Job("paused.7z", DownloadJobStatus.Paused),
            TestData.Job("waiting-1.7z", DownloadJobStatus.AwaitingHandoff),
            TestData.Job("waiting-2.7z", DownloadJobStatus.AwaitingHandoff),
            TestData.Job("failed.7z", DownloadJobStatus.Failed),
            TestData.Job("done.7z", DownloadJobStatus.Completed));
        string atFirst = Screen.SummaryText;

        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(first with { BytesDownloaded = 20 * MiB });
        _harness.Downloads.Publish(second with { BytesDownloaded = 34 * MiB });

        Assert.Equal("3 downloading, 1 queued, 1 paused · 2 waiting for you · 1 failed", atFirst);
        Assert.Equal("3 downloading at 3.00 MB/s, 1 queued, 1 paused · 2 waiting for you · 1 failed", Screen.SummaryText);
    }

    [Fact]
    public async Task ADownloadThatStoppedHasNoSpeedWhenItGoesOn()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = Downloading("a.7z", 16 * MiB);
        await _harness.StartWithAsync(job);
        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(job with { BytesDownloaded = 20 * MiB });
        string measured = Screen.SummaryText;

        _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Paused, BytesDownloaded = 20 * MiB });
        _harness.Downloads.Publish(job with { BytesDownloaded = 20 * MiB });

        Assert.Equal("1 downloading at 2.00 MB/s", measured);
        Assert.Equal("1 downloading", Screen.SummaryText);
    }

    [Fact]
    public async Task SaysWhenNothingIsUnderWay()
    {
        await _harness.StartWithAsync(TestData.Job("done.7z", DownloadJobStatus.Completed), TestData.Job("gone.7z", DownloadJobStatus.Cancelled));

        Assert.Equal("Nothing under way", Screen.SummaryText);
    }

    [Fact]
    public async Task WithoutDownloadsTheresNoSummary()
    {
        await _harness.StartAsync();

        Assert.Equal(string.Empty, Screen.SummaryText);
    }

    [Fact]
    public async Task EachTabCountsAndShowsItsDownloads()
    {
        await _harness.StartWithAsync(
            TestData.Job("queued.7z", createdAt: TestData.Now.AddSeconds(8)),
            TestData.Job("verifying.7z", DownloadJobStatus.Verifying, createdAt: TestData.Now.AddSeconds(7)),
            TestData.Job("paused.7z", DownloadJobStatus.Paused, createdAt: TestData.Now.AddSeconds(6)),
            TestData.Job("waiting.7z", DownloadJobStatus.AwaitingHandoff, createdAt: TestData.Now.AddSeconds(5)),
            TestData.Job("failed.7z", DownloadJobStatus.Failed, createdAt: TestData.Now.AddSeconds(4)),
            TestData.Job("done.7z", DownloadJobStatus.Completed, createdAt: TestData.Now.AddSeconds(3)),
            TestData.Job("cancelled.7z", DownloadJobStatus.Cancelled, createdAt: TestData.Now.AddSeconds(2)));
        List<string> shown = [];
        foreach (DownloadTabViewModel tab in Screen.Tabs)
        {
            Screen.SelectedTab = tab;
            shown.Add($"{tab.Label} {tab.Count}: {string.Join(", ", Screen.ShownJobs.Select(job => job.FileName))}");
        }

        Assert.Equal(
            [
                "All 7: queued.7z, verifying.7z, paused.7z, waiting.7z, failed.7z, done.7z, cancelled.7z",
                "Under way 3: queued.7z, verifying.7z, paused.7z",
                "Waiting for you 1: waiting.7z",
                "Failed 1: failed.7z",
                "Finished 2: done.7z, cancelled.7z",
            ],
            shown);
    }

    [Fact]
    public async Task ATabFollowsItsDownloadsAndSaysWhenItHasNone()
    {
        DownloadJobSnapshot job = TestData.Job("a.7z", DownloadJobStatus.Downloading);
        await _harness.StartWithAsync(job);
        Screen.SelectedTab = Screen.Tabs.Single(tab => tab.Tab == DownloadTab.Failed);
        bool emptyAtFirst = Screen.NothingInTab;

        _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Failed, FailureReason = DownloadFailureReason.Network });

        Assert.True(emptyAtFirst);
        Assert.Equal(["a.7z"], Screen.ShownJobs.Select(shown => shown.FileName));
        Assert.Equal((false, 1), (Screen.NothingInTab, Screen.Tabs[(int)DownloadTab.Failed].Count));
    }

    [Fact]
    public async Task WithoutDownloadsNoTabSaysItHasNone()
    {
        await _harness.StartAsync();

        Screen.SelectedTab = Screen.Tabs[(int)DownloadTab.Failed];

        Assert.False(Screen.NothingInTab);
    }

    [Fact]
    public async Task SaysHowManyFilesWaitForTheUserOnTheirSite()
    {
        DownloadJobSnapshot first = TestData.Job("first.7z", DownloadJobStatus.AwaitingHandoff);
        await _harness.StartWithAsync(first);
        (bool, string) one = (Screen.HasWaiting, Screen.WaitingText);

        _harness.Downloads.Publish(TestData.Job("second.7z", DownloadJobStatus.AwaitingHandoff, createdAt: TestData.Now.AddSeconds(1)));
        string two = Screen.WaitingText;
        _harness.Downloads.Publish(first with { Status = DownloadJobStatus.Downloading, HandoffPage = null });

        Assert.Equal((true, "1 file waits for you on Nexus Mods. Start it from its page there, and ModShelf goes on with the link the site hands over."), one);
        Assert.Equal("2 files wait for you on Nexus Mods. Start each from its page there, and ModShelf goes on with the link the site hands over.", two);
        Assert.StartsWith("1 file waits", Screen.WaitingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingWaitingSaysNothing()
    {
        await _harness.StartWithAsync(TestData.Job("a.7z", DownloadJobStatus.Downloading));

        Assert.Equal((false, string.Empty, false), (Screen.HasWaiting, Screen.WaitingText, Screen.OpenNextPageCommand.CanExecute(null)));
    }

    [Fact]
    public async Task OpenTheNextPageOpensTheOldestWaitingDownloadsPageOneAClick()
    {
        Uri older = new("https://www.nexusmods.com/skyrimspecialedition/mods/1?tab=files&file_id=1&nmm=1");
        Uri newer = new("https://www.nexusmods.com/skyrimspecialedition/mods/1?tab=files&file_id=2&nmm=1");
        DownloadJobSnapshot first = TestData.Job("first.7z", DownloadJobStatus.AwaitingHandoff, createdAt: TestData.Now) with { HandoffPage = older };
        DownloadJobSnapshot second = TestData.Job("second.7z", DownloadJobStatus.AwaitingHandoff, createdAt: TestData.Now.AddSeconds(1)) with { HandoffPage = newer };
        await _harness.StartWithAsync(second, first);

        await Screen.OpenNextPageCommand.ExecuteAsync(null);
        IReadOnlyList<Uri> afterOneClick = [.. _harness.Browser.Opened];
        _harness.Downloads.Publish(first with { Status = DownloadJobStatus.Downloading, HandoffPage = null });
        await Screen.OpenNextPageCommand.ExecuteAsync(null);
        _harness.Downloads.Publish(second with { Status = DownloadJobStatus.Downloading, HandoffPage = null });

        Assert.Equal([older], afterOneClick);
        Assert.Equal([older, newer], _harness.Browser.Opened);
        Assert.False(Screen.OpenNextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task ADownloadWhosePageIsntKnownYetIsntOpened()
    {
        await _harness.StartWithAsync(TestData.Job("first.7z", DownloadJobStatus.AwaitingHandoff) with { HandoffPage = null });

        Assert.Equal((true, false), (Screen.HasWaiting, Screen.OpenNextPageCommand.CanExecute(null)));
    }

    private static DownloadJobSnapshot Downloading(string fileName, long bytes)
        => TestData.Job(fileName, DownloadJobStatus.Downloading) with { BytesDownloaded = bytes, TotalBytes = 400 * MiB };
}
