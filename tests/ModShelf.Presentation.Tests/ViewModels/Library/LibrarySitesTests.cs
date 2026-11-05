using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library once two sites are in use: each game, the open game's heading, the activity and a mod's page show its
/// site, and a site filter beside the game order shows one site's games and counts. Nexus Mods has Skyrim Special Edition,
/// whose mod was checked two hours ago, and Stardew Valley; the example site has Stardew Valley.
/// </summary>
public sealed class LibrarySitesTests : IDisposable
{
    private const long OneMegabyte = 1024 * 1024;

    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private static readonly LibraryMod SkyUiMod = LibraryMod.Restore(
        SkyUi, "SkyUI", "An Author", null, isWatched: true, TestData.Now.AddDays(-30), TestData.Now.AddHours(-2), [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z", size: 3 * OneMegabyte)], []);
    private static readonly LibraryMod SmapiMod = TestData.LibraryMod(Smapi, "SMAPI", files: TestData.LibraryFile(Smapi, "2", "SMAPI.zip", size: OneMegabyte));
    private static readonly LibraryMod CropsMod = TestData.LibraryMod(
        Crops, "Better Crops", files: TestData.LibraryFile(Crops, ExampleSite.BetterCrops11, "better-crops-1.1.zip", size: 2 * OneMegabyte));

    private readonly LibraryHarness _harness = new(new ExampleSite());
    private readonly CultureScope _culture = new("en-US");

    public LibrarySitesTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose()
    {
        _culture.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public async Task WhileOneSiteIsInUseNoSiteShows()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod);

        await ShowAsync(several: false);

        Assert.False(Screen.ShowSites);
        Assert.All(Screen.Games, game => Assert.False(game.ShowSite));
        Assert.False(Screen.HasSiteFilter);
        Assert.Equal(["All sites"], Labels());
        Assert.Equal(1, _harness.Sites.Asked);
    }

    [Fact]
    public async Task OnceTwoSitesAreInUseEachGameAndTheOpenOneShowItsSite()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);

        await ShowAsync(several: true);

        Assert.True(Screen.ShowSites);
        Assert.Equal(
            [(true, "Nexus Mods"), (true, "Nexus Mods"), (true, "Example Mods")],
            new[] { SkyUi.Game, Smapi.Game, Crops.Game }.Select(game => (_harness.Game(game).ShowSite, _harness.Game(game).SiteName)));
        _harness.Row(CropsMod);
        Assert.Equal((true, "Example Mods"), (Screen.OpenGame?.ShowSite, Screen.OpenGame?.SiteName));
    }

    [Fact]
    public async Task AGameAddedOnceSitesShowShowsItsSiteToo()
    {
        await _harness.StartAsync(SkyUiMod);
        await ShowAsync(several: true);

        _harness.Library.Change(Crops, CropsMod);

        Assert.Equal((true, "Example Mods"), (_harness.Game(Crops.Game).ShowSite, _harness.Game(Crops.Game).SiteName));
    }

    [Fact]
    public async Task EachTimeTheScreenShowsItAsksAgainAndSitesGoOnceOneIsInUse()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);
        await ShowAsync(several: true);
        Screen.SelectedSite = Screen.SiteChoices[1];

        await ShowAsync(several: false);

        Assert.Equal(2, _harness.Sites.Asked);
        Assert.False(Screen.ShowSites);
        Assert.All(Screen.Games, game => Assert.False(game.ShowSite));
        Assert.False(Screen.HasSiteFilter);
        Assert.Equal(["All sites"], Labels());
        Assert.Same(Screen.SiteChoices[0], Screen.SelectedSite);
        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task TheSiteFilterOffersAllSitesThenEachSiteOfTheLibrarysGamesByName()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);

        await ShowAsync(several: true);

        Assert.True(Screen.HasSiteFilter);
        Assert.Equal(["All sites", "Example Mods", "Nexus Mods"], Labels());
        Assert.Equal([null, ExampleSite.SourceId, TestData.Nexus], Screen.SiteChoices.Select(choice => choice.Value));
        Assert.Same(Screen.SiteChoices[0], Screen.SelectedSite);
        Assert.Equal("3 games · 3 mods · 6.00 MB on disk · checked 2 hours ago", Screen.SummaryText);
    }

    [Fact]
    public async Task ChoosingASiteShowsItsGamesOnlyAndTheSummaryCountsThemOnly()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);
        await ShowAsync(several: true);

        Screen.SelectedSite = Screen.SiteChoices[1];

        Assert.Equal([Crops.Game], Screen.Games.Select(game => game.Game));
        Assert.Equal("1 game · 1 mod · 2.00 MB on disk", Screen.SummaryText);

        Screen.SelectedSite = Screen.SiteChoices[2];

        Assert.Equal([SkyUi.Game, Smapi.Game], Screen.Games.Select(game => game.Game));
        Assert.Equal("2 games · 2 mods · 4.00 MB on disk · checked 2 hours ago", Screen.SummaryText);

        Screen.SelectedSite = Screen.SiteChoices[0];

        Assert.Equal(3, Screen.Games.Count);
        Assert.Equal("3 games · 3 mods · 6.00 MB on disk · checked 2 hours ago", Screen.SummaryText);
    }

    [Fact]
    public async Task NoSiteChosenIsAllSites()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);
        await ShowAsync(several: true);
        Screen.SelectedSite = Screen.SiteChoices[1];

        // As the site box does when the site it shows is no longer offered.
        Screen.SelectedSite = null;

        Assert.Same(Screen.SiteChoices[0], Screen.SelectedSite);
        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task TheGameFilterNarrowsTheChosenSitesGames()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);
        await ShowAsync(several: true);
        Screen.SelectedSite = Screen.SiteChoices[2];

        Screen.GamesFilter = "stardew";

        Assert.Equal([Smapi.Game], Screen.Games.Select(game => game.Game));
    }

    [Fact]
    public async Task WhileTheLibrarysGamesAreOfOneSiteThereIsNoFilterButSitesStillShow()
    {
        // Two sites are in use, the other through its account.
        await _harness.StartAsync(SkyUiMod, SmapiMod);

        await ShowAsync(several: true);

        Assert.False(Screen.HasSiteFilter);
        Assert.Equal(["All sites"], Labels());
        Assert.All(Screen.Games, game => Assert.Equal((true, "Nexus Mods"), (game.ShowSite, game.SiteName)));
    }

    [Fact]
    public async Task WhenTheChosenSitesLastModGoesAllSitesShowAgain()
    {
        await _harness.StartAsync(SkyUiMod, SmapiMod, CropsMod);
        await ShowAsync(several: true);
        Screen.SelectedSite = Screen.SiteChoices[1];

        _harness.Library.Change(Crops, null);

        Assert.Same(Screen.SiteChoices[0], Screen.SelectedSite);
        Assert.False(Screen.HasSiteFilter);
        Assert.Equal([SkyUi.Game, Smapi.Game], Screen.Games.Select(game => game.Game));
        Assert.Equal("2 games · 2 mods · 4.00 MB on disk · checked 2 hours ago", Screen.SummaryText);
    }

    [Fact]
    public async Task TheActivitySaysEachGamesSiteWhileSitesShow()
    {
        _harness.History.Events.AddRange(
        [
            new(TestData.Now.AddMinutes(-20), LibraryEventKind.Found, Crops, "Better Crops", new ModFileRef(Crops, ExampleSite.BetterCrops11), "better-crops-1.1.zip", null),
            new(TestData.Now.AddMinutes(-30), LibraryEventKind.Found, Smapi, "SMAPI", new ModFileRef(Smapi, "2"), "SMAPI.zip", null),
        ]);
        await _harness.StartAsync(SmapiMod, CropsMod);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Equal([(false, string.Empty), (false, string.Empty)], Entries());

        await ShowAsync(several: true);

        Assert.Equal([(true, "Example Mods"), (true, "Nexus Mods")], Entries());

        await ShowAsync(several: false);

        Assert.Equal([(false, string.Empty), (false, string.Empty)], Entries());
    }

    [Fact]
    public async Task AModsPageShowsItsSiteWhileSitesShow()
    {
        _harness.Lookup.Pages[Crops] = BrowseHarness.Page(BrowseHarness.Info(Crops, "Better Crops"));
        await _harness.StartAsync(SkyUiMod, CropsMod);

        await _harness.Row(CropsMod).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        ModPageViewModel page = Assert.IsType<ModPageViewModel>(Screen.Mod);
        Assert.Equal((false, "Example Mods"), (page.ShowSite, page.SiteName));

        await ShowAsync(several: true);
        Screen.BackCommand.Execute(null);
        await _harness.Row(CropsMod).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        page = Assert.IsType<ModPageViewModel>(Screen.Mod);
        Assert.Equal((true, "Example Mods"), (page.ShowSite, page.SiteName));
    }

    /// <summary>
    /// Shows the screen, as the main window does, with two sites in use or not.
    /// </summary>
    private async Task ShowAsync(bool several)
    {
        _harness.Sites.Several = several;
        Screen.OnShown();
        await Screen.SitesWork;
    }

    private string[] Labels() => [.. Screen.SiteChoices.Select(choice => choice.Label)];

    private (bool ShowSite, string SiteName)[] Entries()
        => [.. Screen.Activity.Days.SelectMany(day => day.Entries).Select(entry => (entry.ShowSite, entry.SiteName))];
}
