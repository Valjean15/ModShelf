using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.ModPages;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// A mod's page opened from Browse shows its site once two sites are in use, as Browse finds each time it opens one.
/// </summary>
public sealed class BrowseSitesTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task WhileOneSiteIsInUseAModsPageDoesNotShowItsSite()
    {
        await _harness.StartAsync();

        ModPageViewModel page = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.Equal((false, "Nexus Mods"), (page.ShowSite, page.SiteName));
    }

    [Fact]
    public async Task OnceTwoSitesAreInUseAModsPageShowsItsSite()
    {
        await _harness.StartAsync();
        ModPageViewModel before = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        _harness.Sites.Several = true;

        ModPageViewModel page = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.False(before.ShowSite);
        Assert.Equal((true, "Nexus Mods"), (page.ShowSite, page.SiteName));
        Assert.Equal(2, _harness.Sites.Asked);
    }
}
