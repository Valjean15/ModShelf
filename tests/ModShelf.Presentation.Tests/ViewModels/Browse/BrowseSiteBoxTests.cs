using ModShelf.Application.State;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse's site box: all sites, or one, whose games alone the game picker offers, without their site's name; remembered
/// between runs.
/// </summary>
public sealed class BrowseSiteBoxTests : IDisposable
{
    private static readonly ModRef Protagonist = new(BrowseHarness.Royal.Game, "440088");
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Smapi = new(BrowseHarness.Stardew.Game, "2400");

    private readonly BrowseHarness _harness = new(banana: true);

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void OffersAllSitesThenEachSiteThatCanSearchByName()
    {
        using BrowseHarness oneSite = new();

        Assert.Equal(["All sites", "GameBanana", "Nexus Mods"], Screen.Sites.Select(site => site.Name));
        Assert.Equal((null, true), (Screen.SelectedSite.Source, Screen.HasSites));
        Assert.Equal(["All sites", "Nexus Mods"], oneSite.Screen.Sites.Select(site => site.Name));
        Assert.False(oneSite.Screen.HasSites);
    }

    [Fact]
    public async Task ASiteNarrowsThePickerToItsGamesWithoutTheirSitesAndAllSitesBringsThemBack()
    {
        await _harness.StartAsync(TestData.LibraryMod(Protagonist, "P5R Female Protagonist"), TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();

        Screen.SelectedSite = Site("Nexus Mods");
        Screen.Picker.Query = "s";
        await Screen.Picker.Finding;
        (string, bool)[] nexus = [.. Screen.Picker.Entries.Select(entry => (entry.Name, entry.ShowSite))];
        string? nexusHint = HintWithNothingTyped();
        bool bananaAsked = _harness.Lookup.Asked.Any(asked => asked.StartsWith($"FindGamesAsync {BrowseHarness.Banana}", StringComparison.Ordinal));
        Screen.Picker.Query = string.Empty;
        Screen.SelectedSite = Site("GameBanana");
        Screen.Picker.Query = "persona";
        await Screen.Picker.Finding;
        string[] banana = [.. Screen.Picker.Entries.Select(entry => entry.Name)];
        string? bananaHint = HintWithNothingTyped();
        Screen.Picker.Query = string.Empty;
        Screen.SelectedSite = Screen.Sites[0];

        Assert.Equal([("Skyrim Special Edition", false), ("Stardew Valley", false)], nexus);
        Assert.Equal("Type to find any of the 3 games on Nexus Mods.", nexusHint);
        Assert.False(bananaAsked);
        Assert.Equal(["Persona 5 Royal (PC)", "Persona 5"], banana);
        Assert.Equal("Type to find a game on GameBanana by its name.", bananaHint);
        Assert.Equal(
            [("Persona 5 Royal (PC)", true), ("Skyrim Special Edition", true)],
            Screen.Picker.Entries.Select(entry => (entry.Name, entry.ShowSite)));
    }

    [Fact]
    public async Task ChoosingASiteChoosesItsGameTheLibraryHasTheMostModsOf()
    {
        await _harness.StartAsync(
            TestData.LibraryMod(Protagonist, "P5R Female Protagonist"),
            TestData.LibraryMod(new ModRef(BrowseHarness.Royal.Game, "414644"), "Restored Outfits Dialogue"),
            TestData.LibraryMod(SkyUi, "SkyUI"),
            TestData.LibraryMod(Smapi, "SMAPI"),
            TestData.LibraryMod(new ModRef(BrowseHarness.Stardew.Game, "1915"), "Content Patcher"));
        await _harness.ShowAsync();
        GameRef? first = Screen.Game?.Game;

        Screen.SelectedSite = Site("Nexus Mods");
        await Screen.RowsWork;

        Assert.Equal(BrowseHarness.Royal.Game, first);
        Assert.Equal(BrowseHarness.Stardew.Game, Screen.Game?.Game);
        Assert.Equal("Stardew Valley", Screen.Picker.Picked?.Name);
        Assert.False(Screen.Picker.Picked?.ShowSite);
    }

    [Fact]
    public async Task ChoosingASiteTheLibraryHasNoGameOfOpensThePickerKeepingTheGame()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();

        Screen.SelectedSite = Site("GameBanana");

        Assert.Equal(BrowseHarness.Skyrim.Game, Screen.Game?.Game);
        Assert.Equal((true, null), (Screen.Picker.IsOpen, Screen.Picker.Picked));
        Screen.Picker.IsOpen = false;
        Assert.Equal(("Skyrim Special Edition", true), (Screen.Picker.Picked?.Name, Screen.Picker.Picked?.ShowSite));
    }

    [Fact]
    public async Task AGameOfAnotherSiteQuickSearchOpensMovesTheBoxToItsSite()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();
        Screen.SelectedSite = Site("Nexus Mods");

        _harness.Screens.OpenInBrowse(BrowseHarness.Persona.Game);
        await Screen.GamesWork;
        await Screen.RememberingSite;

        Assert.Equal(BrowseHarness.Persona.Game, Screen.Game?.Game);
        Assert.Equal("GameBanana", Screen.SelectedSite.Name);
        Assert.Equal("gamebanana", _harness.UiState.State.BrowseSite);
    }

    [Fact]
    public async Task TheSiteChosenIsRememberedAndStartsTheNextRunOnItsGames()
    {
        await _harness.StartAsync(TestData.LibraryMod(Protagonist, "P5R Female Protagonist"), TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();

        Screen.SelectedSite = Site("Nexus Mods");
        await Screen.RememberingSite;
        Screen.SelectedSite = Site("GameBanana");
        await Screen.RememberingSite;
        string? remembered = _harness.UiState.State.BrowseSite;
        Screen.SelectedSite = Screen.Sites[0];
        await Screen.RememberingSite;

        Assert.Equal("gamebanana", remembered);
        Assert.Null(_harness.UiState.State.BrowseSite);
    }

    [Theory]
    [InlineData("nexus", "Nexus Mods")]
    [InlineData("modrinth", "All sites")]
    public async Task ARunStartsOnTheSiteRemembered(string remembered, string shown)
    {
        _harness.UiState.State = UiState.None with { BrowseSite = remembered };
        await _harness.StartAsync(
            TestData.LibraryMod(Protagonist, "P5R Female Protagonist"),
            TestData.LibraryMod(new ModRef(BrowseHarness.Royal.Game, "414644"), "Restored Outfits Dialogue"),
            TestData.LibraryMod(SkyUi, "SkyUI"));

        await _harness.ShowAsync();

        Assert.Equal(shown, Screen.SelectedSite.Name);
        Assert.Equal(shown == "Nexus Mods" ? BrowseHarness.Skyrim.Game : BrowseHarness.Royal.Game, Screen.Game?.Game);
        Assert.Equal(remembered, _harness.UiState.State.BrowseSite);
    }

    [Fact]
    public async Task ASiteThatCantBeRememberedIsLogged()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        _harness.UiState.Refusal = new IOException("The disk is full.");

        Screen.SelectedSite = Site("GameBanana");
        await Screen.RememberingSite;

        Assert.Equal("GameBanana", Screen.SelectedSite.Name);
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("Couldn't remember the site chosen in Browse's site box", StringComparison.Ordinal));
    }

    private SiteChoice Site(string name) => Screen.Sites.Single(site => site.Name == name);

    private string? HintWithNothingTyped()
    {
        string typed = Screen.Picker.Query;
        Screen.Picker.Query = string.Empty;
        string? hint = Screen.Picker.Hint;
        Screen.Picker.Query = typed;
        return hint;
    }
}
