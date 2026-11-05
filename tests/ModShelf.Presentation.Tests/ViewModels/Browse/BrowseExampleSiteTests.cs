using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// The example site, shaped unlike Nexus Mods, beside Nexus Mods on the Browse screen, asked as the app asks it.
/// </summary>
public sealed class BrowseExampleSiteTests : IDisposable
{
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly BrowseHarness _harness = new(example: new ExampleSite());

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsItsGamesBesideNexusModsOnesEachNamingItsSite()
    {
        await _harness.StartAsync();

        await _harness.ShowAsync();

        Assert.Equal(
            [
                "Fallout 4 (Nexus Mods)",
                "Pixel Farm (Example Mods)",
                "Skyrim Special Edition (Nexus Mods)",
                "Stardew Valley (Example Mods)",
                "Stardew Valley (Nexus Mods)",
            ],
            Screen.Games.Select(game => game.Name));
        Assert.False(Screen.GamesFailed);
    }

    [Fact]
    public async Task ItsGameShowsItsNewestAndUpdatedModsButNoTrendingOnes()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(ExampleSite.StardewValley);

        Assert.False(Screen.Rows[0].IsShown);
        Assert.Equal(["Golden Hoes", "Better Crops"], Screen.Rows[1].Mods.Select(mod => mod.Name));
        Assert.Equal(["Better Crops", "Golden Hoes"], Screen.Rows[2].Mods.Select(mod => mod.Name));
    }

    [Fact]
    public async Task SearchingItsGameListsWhatTheSiteFinds()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.SearchText = "crops";

        await _harness.ChooseAndSearchAsync(ExampleSite.StardewValley);

        Assert.Equal(["Better Crops"], _harness.Names());
        Assert.Null(Screen.Problem);
    }

    [Fact]
    public async Task ItsGameOffersOnlyTheOrdersFiltersAndPageSizesItsSiteHas()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(ExampleSite.StardewValley);

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
            Screen.Sorts.Select(sort => sort.Name));
        Assert.Equal([20, 50], Screen.PageSizes.Select(size => size.Count));
        Assert.Equal(4, Screen.TimeWindows.Count);
        Assert.Equal(
            (true, false, false, false, false, string.Empty),
            (Screen.CanSearchByUpdated, Screen.CanSearchByAuthor, Screen.CanSearchByCategory, Screen.CanIncludeAdult, Screen.CanFollow, Screen.ShowFollowedText));
    }

    [Fact]
    public async Task NexusModsGameOffersEverythingAgainInItsWords()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(ExampleSite.StardewValley);

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal(14, Screen.Sorts.Count);
        Assert.Equal(["Most endorsements", "Fewest endorsements"], Screen.Sorts.Where(sort => sort.Sort == SearchSort.Endorsements).Select(sort => sort.Name));
        Assert.Equal([20, 50, 100], Screen.PageSizes.Select(size => size.Count));
        Assert.Equal(
            (true, true, true, true, true, "Tracked mods"),
            (Screen.CanSearchByUpdated, Screen.CanSearchByAuthor, Screen.CanSearchByCategory, Screen.CanIncludeAdult, Screen.CanFollow, Screen.ShowFollowedText));
    }

    [Fact]
    public async Task AnOrderAndPageSizeItsSiteLacksBecomeItsOwnAndOnesItHasAreKept()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
        Screen.SelectedSort = Screen.Sorts.Single(sort => sort.Name == "Largest");
        Screen.SelectedPageSize = Screen.PageSizes.Single(size => size.Count == 100);

        await _harness.ChooseAsync(ExampleSite.StardewValley);

        Assert.Equal(("Best match", 20), (Screen.SelectedSort.Name, Screen.SelectedPageSize.Count));
        Assert.Same(Screen.Sorts[0], Screen.SelectedSort);

        Screen.SelectedSort = Screen.Sorts.Single(sort => sort.Name == "Oldest");
        Screen.SelectedPageSize = Screen.PageSizes.Single(size => size.Count == 50);
        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.Equal(("Oldest", 50), (Screen.SelectedSort.Name, Screen.SelectedPageSize.Count));
        Assert.Contains(Screen.SelectedSort, Screen.Sorts);

        // A game chosen before gets back its own, while the app runs.
        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal(("Largest", 100), (Screen.SelectedSort.Name, Screen.SelectedPageSize.Count));
        Assert.Contains(Screen.SelectedSort, Screen.Sorts);
    }

    [Fact]
    public async Task ItsGamesSavedOrderAndPageSizeItLacksStartOnItsOwn()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer).Add(
                ExampleSite.StardewValley.Game,
                new GameSettings { Browse = new BrowseDefaults { Sort = SearchSort.Size, PageSize = 100, UpdatedWithin = TimeSpan.FromDays(7) } }),
        };
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(ExampleSite.StardewValley);

        Assert.Equal(("Best match", 20, TimeSpan.FromDays(7)), (Screen.SelectedSort.Name, Screen.SelectedPageSize.Count, Screen.SelectedTimeWindow.Within));
    }

    [Fact]
    public async Task AnAuthorTypedForAnotherSitesGameIsLeftOutOfItsSearch()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
        Screen.Author = "Ana";
        Screen.SearchText = "crops";

        await _harness.ChooseAndSearchAsync(ExampleSite.StardewValley);

        Assert.Null(Screen.Problem);
        Assert.Equal(["Better Crops"], _harness.Names());
        Assert.Null(Assert.Single(_harness.Lookup.Searches).Query.Author);
        Assert.Equal("Ana", Screen.Author);
    }

    [Fact]
    public async Task ItsModsLinkOpensItsPageWithEachVersionAMainFile()
    {
        await _harness.StartAsync();
        Screen.SearchText = ExampleSite.ModLink(Crops).AbsoluteUri;

        await Screen.SearchCommand.ExecuteAsync(null);

        ModPageViewModel page = Screen.Mod ?? throw new InvalidOperationException($"The mod didn't open: {Screen.Problem}");
        Assert.Equal("Better Crops", page.Name);
        ModFileSectionViewModel main = page.Sections[0];
        Assert.Equal(
            [ExampleSite.BetterCrops11, ExampleSite.BetterCrops10],
            main.Files.Select(file => file.File.Id));
    }
}
