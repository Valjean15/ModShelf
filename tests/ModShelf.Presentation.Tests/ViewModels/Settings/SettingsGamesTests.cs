using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The Games category: the library's games and those with settings of their own, by name, and a game's page, where each
/// setting uses every game's until it's overridden.
/// </summary>
public sealed class SettingsGamesTests
{
    private static readonly GameRef Skyrim = TestData.Mod("1").Game;
    private static readonly GameRef Stardew = TestData.Mod("1", "stardewvalley").Game;

    private readonly SettingsHarness _harness = new();

    public SettingsGamesTests()
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    [Fact]
    public async Task ListsTheLibrarysGamesAndThoseWithSettingsByName()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"), TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"));
        Store(("fallout4", new GameSettings { Hidden = true, Notify = false }), ("StardewValley", new GameSettings { OldVersions = OldVersionRule.Archive, Browse = new BrowseDefaults { PageSize = 50 } }));

        await StartAsync();

        Assert.Equal(
            [("fallout4", "fallout4", "Hidden · 1 setting overridden"), ("skyrimspecialedition", "Skyrim Special Edition", "Uses the defaults"), ("StardewValley", "Stardew Valley", "2 settings overridden")],
            Screen.Games.Select(row => (row.Game, row.Name, row.Note)));
        Assert.Equal(("F", "S"), (Screen.Games[0].Initial, Screen.Games[1].Initial));
        Assert.False(Screen.HasNoGames);
        Assert.Same(Screen.Games[0], Screen.SelectedGame);
        Assert.Equal("fallout4", Game.Game);
    }

    [Fact]
    public async Task EachLibraryGameShowsTheCoverItsSiteGives()
    {
        Uri skyrimCover = new("https://images.nexusmods.com/images/games/v2/1704/skyrim-hero.jpg");
        Uri stardewCover = new("https://images.nexusmods.com/images/games/v2/1303/stardew-hero.jpg");
        _harness.Catalog.GameCovers["skyrimspecialedition"] = skyrimCover;
        _harness.Catalog.GameCovers["stardewvalley"] = stardewCover;
        _harness.Catalog.GameNames.TryRemove("stardewvalley", out _);
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"), TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"));
        Store(("fallout4", new GameSettings { Hidden = true }));

        await StartAsync();
        await CoversShownAsync();
        GameRowViewModel skyrim = Screen.Games.Single(row => row.Game == "skyrimspecialedition");
        Screen.OnShown();
        await Screen.GamesWork;
        await CoversShownAsync();

        // Stardew Valley's site can't name it, so it has no cover; and a game only the settings have isn't asked.
        Assert.Equal(
            [("fallout4", null), ("skyrimspecialedition", ScriptedPictureCache.FileOf(skyrimCover)), ("stardewvalley", null)],
            Screen.Games.Select(row => (row.Game, row.Cover.File)));
        Assert.Same(skyrim, Screen.Games[1]);
        Assert.Equal([skyrimCover], _harness.Pictures.Cache.Asked);
    }

    [Fact]
    public async Task AGameUsesEveryGamesSettingsUntilItsOwnAreSet()
    {
        _harness.Settings.Stored = new AppSettings { OldVersions = OldVersionRule.KeepLatest(3), UpdateInterval = TimeSpan.FromHours(12), NotifyOfUpdates = false };
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"));

        await StartAsync();

        Assert.Equal("Stardew Valley", Game.Name);
        Assert.Equal((false, false, false, false, false, false), (Game.HasOwnFolders, Game.HasOwnOldVersions, Game.HasOwnChecks, Game.HasOwnNotices, Game.HasOwnBrowse, Game.IsHidden));
        Assert.Equal("Every game's: {source}/{game}/{mod} ({modid})/{file}", Game.EveryGamesFolders);
        Assert.Equal("Every game's: keep the newest 3", Game.EveryGamesOldVersions);
        Assert.Equal("Checked with the other games, every 12 hours.", Game.EveryGamesChecks);
        Assert.Equal("Every game's: no notice.", Game.EveryGamesNotices);
        Assert.Equal("Browse's own: Best match · Updated any time · 20 per page", Game.EveryGamesBrowse);
        Assert.Equal("Hide Stardew Valley in the Library, Updates and Browse", Game.HideText);
        Assert.Equal((true, 3, true, false), (Game.IsKeepLatest, Game.KeepCount, Game.ChecksUpdates, Game.Notify));
        Assert.Equal(
            [
                "With the other games (every 12 hours)", "Every 15 minutes", "Every 30 minutes", "Every hour", "Every 2 hours", "Every 3 hours", "Every 6 hours",
                "Every 12 hours", "Every day", "Every 2 days", "Every 7 days",
            ],
            Game.IntervalChoices.Select(choice => choice.Label));
        Assert.Same(Game.IntervalChoices[0], Game.SelectedInterval);
        Assert.Equal((Game.Sorts[0], Game.TimeWindows[0], Game.PageSizes[0]), (Game.SelectedSort, Game.SelectedTimeWindow, Game.SelectedPageSize));
        await Game.Saving;
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task OldVersionsAreOverriddenChangedAndSetBack()
    {
        _harness.Settings.Stored = new AppSettings { OldVersions = OldVersionRule.Archive };
        await StartWithAsync("stardewvalley");

        await Game.OverrideOldVersionsCommand.ExecuteAsync(null);

        Assert.Equal((true, true), (Game.HasOwnOldVersions, Game.IsArchive));
        Assert.Equal(OldVersionRule.Archive, Own().OldVersions);

        Game.IsKeepLatest = true;
        await Game.Saving;
        Game.KeepCount = 4;
        await Game.Saving;

        Assert.Equal(OldVersionRule.KeepLatest(4), Own().OldVersions);
        Assert.Equal("1 setting overridden", Screen.Games[0].Note);

        await Game.UseDefaultOldVersionsCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (Game.HasOwnOldVersions, Game.IsArchive));
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task UpdateChecksAreOverriddenChangedAndSetBack()
    {
        await StartWithAsync("stardewvalley");

        await Game.OverrideChecksCommand.ExecuteAsync(null);

        Assert.Equal(new GameSettings { CheckUpdates = true }, Own());
        Assert.True(Game.HasOwnChecks);

        Game.SelectedInterval = Game.IntervalChoices.Single(choice => choice.Interval == TimeSpan.FromDays(1));
        await Game.Saving;
        Game.ChecksUpdates = false;
        await Game.Saving;

        Assert.Equal(new GameSettings { CheckUpdates = false, UpdateInterval = TimeSpan.FromDays(1) }, Own());
        Assert.Equal(TimeSpan.FromDays(1), Game.SelectedInterval?.Interval);

        await Game.UseDefaultChecksCommand.ExecuteAsync(null);

        Assert.Equal((false, true, null), (Game.HasOwnChecks, Game.ChecksUpdates, Game.SelectedInterval?.Interval));
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task ChoosingTheOtherGamesIntervalKeepsTheChecksOverridden()
    {
        Store(("stardewvalley", new GameSettings { UpdateInterval = TimeSpan.FromDays(1) }));
        await StartAsync();

        Game.SelectedInterval = Game.IntervalChoices[0];
        await Game.Saving;

        Assert.Equal(new GameSettings { CheckUpdates = true }, Own());
        Assert.True(Game.HasOwnChecks);
    }

    [Fact]
    public async Task AnIntervalNotOfferedIsListedInOrder()
    {
        Store(("stardewvalley", new GameSettings { UpdateInterval = TimeSpan.FromMinutes(90) }));

        await StartAsync();

        Assert.Equal("Every 90 minutes", Game.SelectedInterval?.Label);
        Assert.Same(Game.IntervalChoices[4], Game.SelectedInterval);
        Assert.Equal("Checked with the other games, every 6 hours.", Game.EveryGamesChecks);
    }

    [Fact]
    public async Task NoticesAreOverriddenChangedAndSetBack()
    {
        await StartWithAsync("stardewvalley");
        Assert.Equal("Every game's: a notice when a check finds new updates.", Game.EveryGamesNotices);

        await Game.OverrideNoticesCommand.ExecuteAsync(null);

        Assert.Equal((true, true, true), (Game.HasOwnNotices, Game.Notify, Own().Notify));

        Game.Notify = false;
        await Game.Saving;

        Assert.False(Own().Notify);

        await Game.UseDefaultNoticesCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (Game.HasOwnNotices, Game.Notify));
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task OverriddenNoticesStartAsEveryGamesAre()
    {
        _harness.Settings.Stored = new AppSettings { NotifyOfUpdates = false };
        await StartWithAsync("stardewvalley");

        await Game.OverrideNoticesCommand.ExecuteAsync(null);

        Assert.Equal((true, false, false), (Game.HasOwnNotices, Game.Notify, Own().Notify));
    }

    [Fact]
    public async Task BrowseIsOverriddenChangedAndSetBack()
    {
        await StartWithAsync("stardewvalley");

        await Game.OverrideBrowseCommand.ExecuteAsync(null);

        Assert.True(Game.HasOwnBrowse);
        Assert.Equal(new BrowseDefaults { Sort = SearchSort.Relevance, Direction = SearchDirection.Descending, PageSize = 20 }, Own().Browse);

        Game.SelectedSort = Game.Sorts.Single(sort => sort.Name == "Oldest");
        await Game.Saving;
        Game.SelectedTimeWindow = Game.TimeWindows[3];
        await Game.Saving;
        Game.SelectedPageSize = Game.PageSizes[2];
        await Game.Saving;

        Assert.Equal(
            new BrowseDefaults { Sort = SearchSort.Newest, Direction = SearchDirection.Ascending, UpdatedWithin = TimeSpan.FromDays(30), PageSize = 100 },
            Own().Browse);

        await Game.UseDefaultBrowseCommand.ExecuteAsync(null);

        Assert.Equal((false, Game.Sorts[0], Game.PageSizes[0]), (Game.HasOwnBrowse, Game.SelectedSort, Game.SelectedPageSize));
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task ShowsAGamesOwnBrowseDefaults()
    {
        Store(("stardewvalley", new GameSettings { Browse = new BrowseDefaults { Sort = SearchSort.Name, UpdatedWithin = TimeSpan.FromDays(1) } }));

        await StartAsync();

        Assert.Equal(("Name, A to Z", "Updated in the last 24 hours", 20), (Game.SelectedSort?.Name, Game.SelectedTimeWindow?.Name, Game.SelectedPageSize?.Count));
    }

    [Fact]
    public async Task HidingAGameHidesItEverywhereAtOnce()
    {
        await StartWithAsync("stardewvalley");

        Game.IsHidden = true;
        await Game.Saving;

        Assert.Equal((true, true, "Hidden"), (Own().Hidden, _harness.Hidden.IsHidden("StardewValley"), Screen.Games[0].Note));

        Game.IsHidden = false;
        await Game.Saving;

        Assert.False(_harness.Hidden.IsHidden("stardewvalley"));
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task AFailedChangeIsSaidOnTheGamesPage()
    {
        await StartWithAsync("stardewvalley");
        _harness.Settings.SaveRefusal = new IOException("The disk is full.");

        await Game.OverrideNoticesCommand.ExecuteAsync(null);

        Assert.Equal("The disk is full.", Game.Problem);
        Assert.False(Game.HasOwnNotices);
    }

    [Fact]
    public async Task TheGearOnAGameOpensItsSettings()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"), TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"));
        await StartAsync();
        Screen.SearchText = "theme";

        _harness.GameSettings.Open(Stardew);

        Assert.Equal((SettingsCategory.Games, string.Empty), (Screen.SelectedCategory?.Category, Screen.SearchText));
        Assert.Equal("stardewvalley", Screen.SelectedGame?.Game);
        Assert.Equal("Stardew Valley", Game.Name);
        Assert.Equal(Presentation.Services.Screen.Settings, _harness.Navigation.Current);
    }

    [Fact]
    public async Task TheGearOnAGameNotListedYetListsIt()
    {
        await _harness.StartAsync();

        _harness.GameSettings.Open(Skyrim);
        await Screen.GamesWork;

        Assert.Equal(("skyrimspecialedition", "Skyrim Special Edition"), (Assert.Single(Screen.Games).Game, Game.Name));
    }

    [Fact]
    public async Task ALibraryThatCantBeReadStillListsTheGamesWithSettings()
    {
        _harness.Library.ListRefusal = new InvalidDataException("The library can't be read.");
        Store(("fallout4", new GameSettings { Hidden = true }));

        await StartAsync();

        Assert.Equal(["fallout4"], Screen.Games.Select(row => row.Game));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Warning: Couldn't list the library's games for the Settings screen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithNoGameNothingIsChosen()
    {
        await StartAsync();

        Assert.Equal((true, null, null), (Screen.HasNoGames, Screen.SelectedGame, Screen.Game));
    }

    private void Store(params (string Game, GameSettings Own)[] games)
        => _harness.Settings.Stored = new AppSettings { Games = games.ToImmutableDictionary(game => game.Game, game => game.Own, StringComparer.OrdinalIgnoreCase) };

    private GameSettings Own() => _harness.Settings.Stored.GameFor(Game.Game);

    /// <summary>
    /// Starts the screen, as the main window does, then shows it, as the sidebar does, and waits for its games.
    /// </summary>
    private Task CoversShownAsync()
        => Task.WhenAll(Screen.Games.Select(row => row.Cover.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
    }

    /// <summary>
    /// Starts the screen with a game of the library chosen.
    /// </summary>
    private async Task StartWithAsync(string game)
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1", game), "A mod"));
        await StartAsync();
        Assert.Equal(game, Game.Game);
    }
}
