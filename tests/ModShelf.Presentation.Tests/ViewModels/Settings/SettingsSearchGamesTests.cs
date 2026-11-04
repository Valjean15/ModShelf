using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Searching the Settings screen finds games by name too: their section shows with only those games, the first chosen.
/// The search reads names as every list does: the starts of words, in any order.
/// </summary>
public sealed class SettingsSearchGamesTests
{
    private static readonly GameRef Skyrim = TestData.Mod("1").Game;
    private static readonly GameRef Stalker = TestData.Mod("1", "stalker2heartofchornobyl").Game;

    private readonly SettingsHarness _harness = new();

    public SettingsSearchGamesTests()
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameNames["stalker2heartofchornobyl"] = "S.T.A.L.K.E.R. 2: Heart of Chornobyl";
    }

    private SettingsViewModel Screen => _harness.Screen;

    [Fact]
    public async Task AGamesNameFindsItsSettingsAlone()
    {
        await StartAsync();

        Screen.SearchText = "stalker 2";

        Assert.True(Screen.GamesSection.IsShown);
        Assert.Equal([Stalker], Screen.Games.Select(row => row.Game));
        Assert.Equal(Stalker, Screen.SelectedGame?.Game);
        Assert.True(Screen.Categories.Single(category => category.Category == SettingsCategory.Games).HasMatches);
        Assert.Null(Screen.NothingMatches);
    }

    [Fact]
    public async Task AGameWhoseNameIsReadAfterTheSearchIsFoundThen()
    {
        _harness.Catalog.HoldGames = new TaskCompletionSource();
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("5", "stalker2heartofchornobyl"), "Anomaly Fix"));
        await _harness.StartAsync();
        Screen.OnShown();
        Screen.SearchText = "stalker 2";
        bool before = Screen.GamesSection.IsShown;

        _harness.Catalog.HoldGames.SetResult();
        await Screen.GamesWork;

        Assert.False(before);
        Assert.True(Screen.GamesSection.IsShown);
        Assert.Equal([Stalker], Screen.Games.Select(row => row.Game));
    }

    [Fact]
    public async Task WordsOfTheGamesSectionListEveryGame()
    {
        await StartAsync();

        Screen.SearchText = "notices";

        Assert.True(Screen.GamesSection.IsShown);
        Assert.Equal(2, Screen.Games.Count);
    }

    [Fact]
    public async Task ANameMatchingNoGameOrSettingFindsNothing()
    {
        await StartAsync();

        Screen.SearchText = "chorno zzz";

        Assert.False(Screen.GamesSection.IsShown);
        Assert.Equal("No setting matches “chorno zzz”.", Screen.NothingMatches);
        Assert.Equal(2, Screen.Games.Count);
    }

    [Fact]
    public async Task EveryGameAgainOnceTheSearchIsCleared()
    {
        await StartAsync();
        Screen.SearchText = "skyrim";
        GameRef[] found = [.. Screen.Games.Select(row => row.Game)];

        Screen.SearchText = string.Empty;

        Assert.Equal([Skyrim], found);
        Assert.Equal(2, Screen.Games.Count);
    }

    [Fact]
    public async Task ASectionIsFoundByTheStartsOfItsWords()
    {
        await StartAsync();

        Screen.SearchText = "heme";

        Assert.False(Screen.ThemeSection.IsShown);
    }

    private async Task StartAsync()
    {
        _harness.Library.Add(
            TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"),
            TestData.LibraryMod(TestData.Mod("5", "stalker2heartofchornobyl"), "Anomaly Fix"));
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
    }
}
