using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// What games' own settings change on Browse: the hidden ones are left out of the game box, and choosing a game starts
/// the toolbar as its settings say.
/// </summary>
public sealed class BrowseGameSettingsTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task HiddenGamesAreLeftOutOfTheGameBox()
    {
        _harness.Settings.Stored = Settings(("StardewValley", new GameSettings { Hidden = true }));

        await _harness.StartAsync();
        await _harness.ShowAsync();

        Assert.Equal(["Fallout 4", "Skyrim Special Edition"], Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task AHiddenGameIsntChosenForTheLibrarysMostMods()
    {
        _harness.Settings.Stored = Settings(("stardewvalley", new GameSettings { Hidden = true }));

        await _harness.StartAsync(
            TestData.LibraryMod(TestData.Mod("1", "stardewvalley"), "One"),
            TestData.LibraryMod(TestData.Mod("2", "stardewvalley"), "Two"),
            TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        await _harness.ShowAsync();

        Assert.Equal(BrowseHarness.Skyrim.Game, Screen.Game?.Game);
    }

    [Fact]
    public async Task HidingAGameOnTheSettingsScreenLeavesItOutAtOnce()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        _harness.Hidden.Show(Settings(("fallout4", new GameSettings { Hidden = true })));

        Assert.Equal(["Skyrim Special Edition", "Stardew Valley"], Screen.Games.Select(game => game.Name));

        _harness.Hidden.Show(new AppSettings());

        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task ChoosingAGameStartsTheToolbarAsItsSettingsSay()
    {
        _harness.Settings.Stored = Settings((
            "skyrimspecialedition",
            new GameSettings
            {
                Browse = new BrowseDefaults { Sort = SearchSort.Updated, Direction = SearchDirection.Ascending, UpdatedWithin = TimeSpan.FromDays(7), PageSize = 50 },
            }));
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAndSearchAsync(BrowseHarness.Skyrim);

        Assert.Equal(("Longest since updated", "Updated in the last week", 50), (Screen.SelectedSort.Name, Screen.SelectedTimeWindow.Name, Screen.SelectedPageSize.Count));
        SearchQuery query = Assert.Single(_harness.Lookup.Searches).Query;
        Assert.Equal(
            (SearchSort.Updated, SearchDirection.Ascending, TestData.Now.AddDays(-7), 50),
            (query.Sort, query.Direction, query.UpdatedSince, query.Count));
    }

    [Fact]
    public async Task WhatTheGameLeavesOutIsBrowsesOwnAndItsOrderGoesItsOwnWay()
    {
        _harness.Settings.Stored = Settings(
            ("skyrimspecialedition", new GameSettings { Browse = new BrowseDefaults { PageSize = 100 } }),
            ("fallout4", new GameSettings { Browse = new BrowseDefaults { Sort = SearchSort.Endorsements } }));
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.SelectedSort = Screen.Sorts[3];
        Screen.SelectedTimeWindow = Screen.TimeWindows[2];

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal((Screen.Sorts[0], Screen.TimeWindows[0], 100), (Screen.SelectedSort, Screen.SelectedTimeWindow, Screen.SelectedPageSize.Count));

        await _harness.ChooseAsync(BrowseHarness.Fallout);

        Assert.Equal(("Most endorsements", 20), (Screen.SelectedSort.Name, Screen.SelectedPageSize.Count));
    }

    [Fact]
    public async Task AGameWithoutSettingsLeavesTheToolbarAsItWas()
    {
        _harness.Settings.Stored = Settings(("skyrimspecialedition", new GameSettings { Browse = new BrowseDefaults { PageSize = 50 } }));
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.Equal(50, Screen.SelectedPageSize.Count);
    }

    [Fact]
    public async Task ChoosingAGameStartsTheToolbarWithoutSearching()
    {
        _harness.Settings.Stored = Settings(("skyrimspecialedition", new GameSettings { Browse = new BrowseDefaults { PageSize = 50 } }));
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.True(Screen.IsShowingRows);
        Assert.DoesNotContain(_harness.Lookup.Searches, search => search.Query.Count != BrowseViewModel.RowSize);
    }

    [Fact]
    public async Task ASearchStartedBeforeTheGamesSettingsAreReadKeepsItsToolbar()
    {
        _harness.Settings.Stored = Settings(("skyrimspecialedition", new GameSettings { Browse = new BrowseDefaults { PageSize = 50 } }));
        await _harness.StartAsync();
        await _harness.ShowAsync();
        TaskCompletionSource reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Settings.ReadHeld = reading;

        Screen.Game = Screen.Games.Single(choice => choice.Game == BrowseHarness.Skyrim.Game);
        await Screen.SearchCommand.ExecuteAsync(null);
        reading.SetResult();
        await Screen.DefaultsWork;

        Assert.Equal(20, Screen.SelectedPageSize.Count);
        Assert.Equal(20, _harness.Lookup.Searches[^1].Query.Count);
    }

    [Fact]
    public async Task ChoosingAGameReadsTheHiddenGamesAgain()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        // As when the settings file is edited by hand while the app runs.
        _harness.Settings.Stored = Settings(("fallout4", new GameSettings { Hidden = true }));
        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.Equal(["Skyrim Special Edition", "Stardew Valley"], Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task SettingsThatCantBeReadLeaveTheToolbarAsItWas()
    {
        _harness.Settings.ReadRefusal = new InvalidDataException("Not JSON.");
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal((Screen.Sorts[0], 20), (Screen.SelectedSort, Screen.SelectedPageSize.Count));
        Assert.Equal(3, Screen.Games.Count);
    }

    /// <summary>
    /// Nexus Mods' games' own settings, by their keys in any case.
    /// </summary>
    private static AppSettings Settings(params (string Game, GameSettings Own)[] games)
        => new() { Games = games.ToDictionary(game => new GameRef(TestData.Nexus, game.Game), game => game.Own, AppSettings.GameComparer) };
}
