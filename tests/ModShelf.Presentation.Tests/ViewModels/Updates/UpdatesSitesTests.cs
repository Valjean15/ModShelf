using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

/// <summary>
/// Each game's heading on the Updates screen shows its site once two sites are in use, as the screen finds each time
/// it's shown.
/// </summary>
public sealed class UpdatesSitesTests : IDisposable
{
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly UpdatesHarness _harness = new(new ExampleSite());

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task WhileOneSiteIsInUseNoHeadingShowsItsSite()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"));

        await _harness.ShowAsync();

        Assert.False(Screen.ShowSites);
        Assert.Equal((false, "Nexus Mods"), Heading(Smapi.Game));
        Assert.Equal(1, _harness.Sites.Asked);
    }

    [Fact]
    public async Task OnceTwoSitesAreInUseEachHeadingShowsItsSite()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(Crops, "Better Crops", TestData.Now, "v5e6f"));
        _harness.Sites.Several = true;

        await _harness.ShowAsync();

        Assert.True(Screen.ShowSites);
        Assert.Equal((true, "Nexus Mods"), Heading(Smapi.Game));
        Assert.Equal((true, "Example Mods"), Heading(Crops.Game));
    }

    [Fact]
    public async Task AHeadingAddedOnceSitesShowShowsItsSiteToo()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"));
        _harness.Sites.Several = true;
        await _harness.ShowAsync();

        _harness.Library.Change(Crops, UpdatesHarness.ModWithUpdates(Crops, "Better Crops", TestData.Now, "v5e6f"));

        Assert.Equal((true, "Example Mods"), Heading(Crops.Game));
    }

    [Fact]
    public async Task SitesGoOnceOneIsInUseAgain()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(Crops, "Better Crops", TestData.Now, "v5e6f"));
        _harness.Sites.Several = true;
        await _harness.ShowAsync();
        _harness.Sites.Several = false;

        await _harness.ShowAsync();

        Assert.Equal(2, _harness.Sites.Asked);
        Assert.False(Screen.ShowSites);
        Assert.Equal([false, false], Screen.Games.Select(game => game.ShowSite));
    }

    private (bool ShowSite, string SiteName) Heading(GameRef game)
    {
        UpdateGameViewModel heading = Assert.Single(Screen.Games, shown => shown.Game == game);
        return (heading.ShowSite, heading.SiteName);
    }
}
