using ModShelf.Application.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// Each download from a mod site shows its site once two sites are in use, as the screen finds each time it's shown.
/// A direct link's download never does: direct links aren't a site.
/// </summary>
public sealed class DownloadsSitesTests : IDisposable
{
    private static readonly SourceId Direct = new("direct");

    private readonly DownloadsHarness _harness = new(new FakeSource(Direct, "Direct links"));

    private DownloadsViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task WhileOneSiteIsInUseNoDownloadShowsItsSite()
    {
        DownloadJobSnapshot job = TestData.Job();
        await _harness.StartWithAsync(job);

        await ShowAsync(several: false);

        Assert.False(Screen.ShowSites);
        Assert.Equal((false, "Nexus Mods"), (_harness.Row(job).ShowSite, _harness.Row(job).SiteName));
        Assert.Equal(1, _harness.Sites.Asked);
    }

    [Fact]
    public async Task OnceTwoSitesAreInUseADownloadFromASiteShowsItButADirectLinksDoesNot()
    {
        DownloadJobSnapshot fromSite = TestData.Job();
        DownloadJobSnapshot fromLink = DirectJob();
        await _harness.StartWithAsync(fromSite, fromLink);

        await ShowAsync(several: true);

        Assert.True(Screen.ShowSites);
        Assert.Equal((true, "Nexus Mods"), (_harness.Row(fromSite).ShowSite, _harness.Row(fromSite).SiteName));
        Assert.False(_harness.Row(fromLink).ShowSite);
    }

    [Fact]
    public async Task ADownloadAddedOnceSitesShowShowsItsSiteToo()
    {
        await _harness.StartAsync();
        await ShowAsync(several: true);
        DownloadJobSnapshot fromSite = TestData.Job();
        DownloadJobSnapshot fromLink = DirectJob();

        _harness.Downloads.Publish(fromSite);
        _harness.Downloads.Publish(fromLink);

        Assert.True(_harness.Row(fromSite).ShowSite);
        Assert.False(_harness.Row(fromLink).ShowSite);
    }

    [Fact]
    public async Task SitesGoOnceOneIsInUseAgain()
    {
        DownloadJobSnapshot fromSite = TestData.Job();
        DownloadJobSnapshot fromLink = DirectJob();
        await _harness.StartWithAsync(fromSite, fromLink);
        await ShowAsync(several: true);

        await ShowAsync(several: false);

        Assert.Equal(2, _harness.Sites.Asked);
        Assert.False(Screen.ShowSites);
        Assert.Equal([false, false], Screen.Jobs.Select(job => job.ShowSite));
    }

    private static DownloadJobSnapshot DirectJob()
        => TestData.Job("tool.zip", createdAt: TestData.Now.AddMinutes(-1)) with
        {
            File = new ModFileRef(new ModRef(new GameRef(Direct, "web"), "files.example.com"), "tool.zip"),
        };

    /// <summary>
    /// Shows the screen, as the main window does, with two sites in use or not.
    /// </summary>
    private async Task ShowAsync(bool several)
    {
        _harness.Sites.Several = several;
        Screen.OnShown();
        await Screen.SitesWork;
    }
}
