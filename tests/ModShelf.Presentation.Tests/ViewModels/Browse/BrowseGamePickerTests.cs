using ModShelf.Application.Settings;
using ModShelf.Application.State;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse's game picker: the sites' games but hidden ones, the library's first; choosing one chooses Browse's game; and
/// the searches made, remembered between runs, to search again.
/// </summary>
public sealed class BrowseGamePickerTests : IDisposable
{
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Patcher = TestData.Mod("1915", "stardewvalley");
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task YourGamesAreTheLibrarysAndFollowItsChanges()
    {
        await _harness.StartAsync(TestData.LibraryMod(Smapi, "SMAPI"), TestData.LibraryMod(Patcher, "Content Patcher"), TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();

        Assert.Equal(
            [("Stardew Valley", "2 mods in your library"), ("Skyrim Special Edition", "1 mod in your library")],
            Screen.Picker.Entries.Select(entry => (entry.Name, entry.Meta)));
        Assert.Equal("Type to find any of the 3 games on Nexus Mods.", Screen.Picker.Hint);

        _harness.Library.Change(Smapi, null);
        _harness.Library.Change(Patcher, null);

        Assert.Equal(["Skyrim Special Edition"], Screen.Picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task TheGameChosenIsTheChip()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        await _harness.ShowAsync();

        Assert.Equal(BrowseHarness.Skyrim.Game, Screen.Game?.Game);
        Assert.Equal(("Skyrim Special Edition", false), (Screen.Picker.Picked?.Name, Screen.Picker.Picked?.ShowSite));
    }

    [Fact]
    public async Task ChoosingAGameInThePickerChoosesItsMods()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        Screen.Picker.Query = "fall";
        Screen.Picker.ChooseCommand.Execute(null);
        await Screen.RowsWork;

        Assert.Equal(BrowseHarness.Fallout.Game, Screen.Game?.Game);
        Assert.Equal(BrowseHarness.Fallout.Game, Screen.Picker.Picked?.Game);
        Assert.True(Screen.IsShowingRows);
        Assert.Contains(_harness.Lookup.Searches, search => search.Game == BrowseHarness.Fallout.Game);
    }

    [Fact]
    public async Task HiddenGamesArentOffered()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [BrowseHarness.Fallout.Game] = new GameSettings { Hidden = true } },
        };
        await _harness.StartAsync();
        await _harness.ShowAsync();

        Screen.Picker.Query = "fall";

        Assert.Empty(Screen.Picker.Entries);

        Screen.Picker.Query = string.Empty;

        Assert.Equal("Type to find any of the 2 games on Nexus Mods.", Screen.Picker.Hint);
    }

    [Fact]
    public async Task TheRecentSearchesAreReadAsItStarts()
    {
        _harness.UiState.State = UiState.None with { BrowseSearches = ["skyui", "armour"] };

        await _harness.StartAsync();

        Assert.Equal(["skyui", "armour"], Screen.Picker.Recent.Select(search => search.Text));
    }

    [Fact]
    public async Task EachSearchIsRememberedTheNewestFirst()
    {
        await StartOnSkyrimAsync();

        await SearchAsync("skyui");
        await SearchAsync(" armour ");
        await SearchAsync("SkyUI");

        Assert.Equal(["SkyUI", "armour"], _harness.UiState.State.BrowseSearches);
        Assert.Equal(["SkyUI", "armour"], Screen.Picker.Recent.Select(search => search.Text));
    }

    [Fact]
    public async Task NeitherALinkNorNoWordsAreRemembered()
    {
        await StartOnSkyrimAsync();

        await SearchAsync("https://www.nexusmods.com/skyrimspecialedition/mods/999999");
        await SearchAsync("   ");

        Assert.Empty(_harness.UiState.State.BrowseSearches);
    }

    [Fact]
    public async Task ARecentSearchSearchesAgainInTheChosenGame()
    {
        _harness.UiState.State = UiState.None with { BrowseSearches = ["skyui", "armour"] };
        await StartOnSkyrimAsync();
        int searches = _harness.Lookup.Searches.Count;

        Screen.Picker.Recent[1].SearchCommand.Execute(null);
        await Screen.Listing;
        await Screen.RememberingSearch;

        Assert.Equal("armour", Screen.SearchText);
        Assert.Equal(("armour", BrowseHarness.Skyrim.Game), (_harness.Lookup.Searches.Skip(searches).Single().Query.Text, Screen.Game?.Game));
        Assert.Equal(["armour", "skyui"], _harness.UiState.State.BrowseSearches);
        Assert.False(Screen.Picker.IsOpen);
    }

    [Fact]
    public async Task ASearchThatCantBeRememberedIsLoggedAndStillSearched()
    {
        await StartOnSkyrimAsync();
        _harness.UiState.Refusal = new IOException("The disk is full.");
        int searches = _harness.Lookup.Searches.Count;

        await SearchAsync("skyui");

        Assert.Single(_harness.Lookup.Searches.Skip(searches));
        Assert.StartsWith(
            "Warning: Couldn't remember Browse's search, so the game picker doesn't offer it System.IO.IOException: The disk is full.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.Empty(Screen.Picker.Recent);
    }

    [Fact]
    public async Task GamesOfTwoSitesShowTheirSites()
    {
        using BrowseHarness harness = new(example: new ExampleSite());
        await harness.StartAsync();
        await harness.ShowAsync();

        harness.Screen.Picker.Query = "stardew";

        Assert.Equal(
            [("Stardew Valley", "Example Mods", true), ("Stardew Valley", "Nexus Mods", true)],
            harness.Screen.Picker.Entries.Select(entry => (entry.Name, entry.SiteName, entry.ShowSite)).OrderBy(entry => entry.SiteName, StringComparer.Ordinal));
        Assert.StartsWith("Games on Example Mods and Nexus Mods", harness.Screen.Picker.Entries[0].Heading, StringComparison.Ordinal);
    }

    private async Task StartOnSkyrimAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }

    private async Task SearchAsync(string text)
    {
        Screen.SearchText = text;
        await Screen.SearchCommand.ExecuteAsync(null);
        await Screen.RememberingSearch;
    }
}
