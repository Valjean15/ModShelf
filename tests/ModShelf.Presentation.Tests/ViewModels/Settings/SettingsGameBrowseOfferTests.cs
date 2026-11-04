using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// How Browse starts for a game offers only what its site's search can do, as Browse does, and a stored choice the site
/// lacks, as one set by hand, shows Browse's own without being changed.
/// </summary>
public sealed class SettingsGameBrowseOfferTests
{
    private static readonly GameRef NexusStardew = TestData.Mod("2400", "stardewvalley").Game;
    private static readonly GameRef ExampleStardew = ExampleSite.StardewValley.Game;

    private readonly SettingsHarness _harness = new(example: new ExampleSite());

    public SettingsGameBrowseOfferTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    [Fact]
    public async Task AGamesBrowseOffersOnlyWhatItsSiteCan()
    {
        await StartAsync();

        Choose(ExampleStardew);

        Assert.Equal(
            [
                "Best match",
                "Most downloaded",
                "Least downloaded",
                "Recently updated",
                "Longest since updated",
                "Newest",
                "Oldest",
                "Name, A to Z",
                "Name, Z to A",
            ],
            Game.Sorts.Select(sort => sort.Name));
        Assert.Equal([20, 50], Game.PageSizes.Select(size => size.Count));
        Assert.Equal((true, 4), (Game.CanSearchByUpdated, Game.TimeWindows.Count));
        Assert.Equal("Browse's own: Best match · Updated any time · 20 per page", Game.EveryGamesBrowse);
    }

    [Fact]
    public async Task AnotherSitesGameOffersItsOwnInItsWords()
    {
        await StartAsync();

        Choose(NexusStardew);

        Assert.Equal(14, Game.Sorts.Count);
        Assert.Contains(Game.Sorts, sort => sort.Name == "Most endorsements");
        Assert.Equal([20, 50, 100], Game.PageSizes.Select(size => size.Count));
    }

    [Fact]
    public async Task AStoredChoiceTheSiteLacksShowsBrowsesOwnUnchanged()
    {
        BrowseDefaults stored = new() { Sort = SearchSort.Size, PageSize = 100 };
        _harness.Settings.Stored = new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer).Add(ExampleStardew, new GameSettings { Browse = stored }),
        };
        await StartAsync();

        Choose(ExampleStardew);

        Assert.Equal(("Best match", 20), (Game.SelectedSort?.Name, Game.SelectedPageSize?.Count));
        Assert.True(Game.HasOwnBrowse);
        Assert.Equal(stored, _harness.Settings.Stored.GameFor(ExampleStardew).Browse);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task SayingHowBrowseStartsStartsWithItsSitesOwn()
    {
        await StartAsync();
        Choose(ExampleStardew);

        await Game.OverrideBrowseCommand.ExecuteAsync(null);

        Assert.Equal(
            new BrowseDefaults { Sort = SearchSort.Relevance, Direction = SearchDirection.Descending, PageSize = 20 },
            _harness.Settings.Stored.GameFor(ExampleStardew).Browse);
    }

    [Fact]
    public async Task BrowsesOwnIsTheSitesOwnOrderThoughBrowseListsItLaterAndNoUpdateWindowsWithoutThem()
    {
        SettingsHarness harness = new(example: new ExampleSite
        {
            Capabilities = new([new(SearchSort.Newest, SearchDirection.Descending), new(SearchSort.Relevance, SearchDirection.Descending)], SearchFilters.Text, 50),
        });
        harness.Library.Add(TestData.LibraryMod(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), "Better Crops"));
        await harness.StartAsync();
        harness.Screen.OnShown();
        await harness.Screen.GamesWork;
        harness.Screen.SelectedGame = harness.Screen.Games.Single(row => row.Game == ExampleStardew);
        GameSettingsViewModel game = Assert.IsType<GameSettingsViewModel>(harness.Screen.Game);

        await game.OverrideBrowseCommand.ExecuteAsync(null);

        Assert.Equal(["Best match", "Newest"], game.Sorts.Select(sort => sort.Name));
        Assert.Equal((false, 1), (game.CanSearchByUpdated, game.TimeWindows.Count));
        Assert.Equal("Browse's own: Newest · Updated any time · 20 per page", game.EveryGamesBrowse);
        Assert.Equal((SearchSort.Newest, SearchDirection.Descending), (harness.Settings.Stored.GameFor(ExampleStardew).Browse?.Sort, harness.Settings.Stored.GameFor(ExampleStardew).Browse?.Direction));
        Assert.Equal("Newest", game.SelectedSort?.Name);
    }

    private void Choose(GameRef game) => Screen.SelectedGame = Screen.Games.Single(row => row.Game == game);

    /// <summary>
    /// Starts the screen with Stardew Valley from Nexus Mods and from the example site in the library.
    /// </summary>
    private async Task StartAsync()
    {
        _harness.Library.Add(
            TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"),
            TestData.LibraryMod(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), "Better Crops"));
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
    }
}
