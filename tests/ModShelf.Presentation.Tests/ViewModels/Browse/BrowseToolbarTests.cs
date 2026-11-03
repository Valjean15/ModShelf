using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse's toolbar: the time window, the game's categories and the page size, each searching again when changed, and
/// what a mod says besides in the order it's listed in.
/// </summary>
public sealed class BrowseToolbarTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OffersEachTimeWindowEveryCategoryAndThreePageSizes()
    {
        await _harness.StartAsync();

        Assert.Equal(
            ["Updated any time", "Updated in the last 24 hours", "Updated in the last week", "Updated in the last month"],
            Screen.TimeWindows.Select(window => window.ToString()));
        Assert.Equal([null, TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30)], Screen.TimeWindows.Select(window => window.Within));
        Assert.Same(Screen.TimeWindows[0], Screen.SelectedTimeWindow);
        Assert.Equal(["All categories"], Screen.Categories.Select(category => category.ToString()));
        Assert.Null(Assert.Single(Screen.Categories).Category);
        Assert.Same(Screen.Categories[0], Screen.SelectedCategory);
        Assert.Equal([(20, "20 per page"), (50, "50 per page"), (100, "100 per page")], Screen.PageSizes.Select(size => (size.Count, size.ToString())));
        Assert.Same(Screen.PageSizes[0], Screen.SelectedPageSize);
        Assert.Equal(
            ["Most unique downloads", "Largest", "Smallest", "Name, A to Z", "Name, Z to A"],
            Screen.Sorts.Skip(9).Select(sort => sort.ToString()));
    }

    [Fact]
    public async Task ChoosingAGameReadsItsCategoriesByNameWithoutSearching()
    {
        SkyrimCategories("Visuals", "armour", "Audio");

        await StartAsync();

        Assert.Equal(["All categories", "armour", "Audio", "Visuals"], Screen.Categories.Select(category => category.Name));
        Assert.Equal("armour", Screen.Categories[1].Category);
        Assert.Same(Screen.Categories[0], Screen.SelectedCategory);
        Assert.True(Screen.IsShowingRows);
        Assert.DoesNotContain(_harness.Lookup.Searches, search => search.Query.Count != BrowseViewModel.RowSize);
    }

    [Fact]
    public async Task AnotherGameChoosesEveryCategoryAgainAndOffersItsOwn()
    {
        SkyrimCategories("Armour");
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameCategories["stardewvalley"] = ["Crops"];
        await StartAsync();
        Screen.SelectedCategory = Screen.Categories[1];
        await Screen.Listing;
        int searches = _harness.Lookup.Searches.Count;

        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.Equal(["All categories", "Crops"], Screen.Categories.Select(category => category.Name));
        Assert.Same(Screen.Categories[0], Screen.SelectedCategory);
        Assert.True(Screen.IsShowingRows);

        // Only the new game's rows.
        Assert.All(_harness.Lookup.Searches.Skip(searches), search => Assert.Equal((BrowseHarness.Stardew.Game, BrowseViewModel.RowSize), (search.Game, search.Query.Count)));
    }

    [Fact]
    public async Task TheCategoriesOfAGameNoLongerChosenArentOffered()
    {
        SkyrimCategories("Armour");
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameCategories["stardewvalley"] = ["Crops"];
        await _harness.StartAsync();
        await _harness.ShowAsync();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Catalog.HoldGames = held;
        Screen.Game = Screen.Games.Single(choice => choice.Game == BrowseHarness.Skyrim.Game);
        Task skyrim = Screen.CategoriesWork;

        _harness.Catalog.HoldGames = null;
        await _harness.ChooseAsync(BrowseHarness.Stardew);
        held.SetResult();
        await skyrim;

        Assert.Equal(["All categories", "Crops"], Screen.Categories.Select(category => category.Name));
    }

    [Fact]
    public async Task CategoriesTheSiteCantSayLeaveEveryCategory()
    {
        _harness.Catalog.Refusal = new AuthenticationFailedException();

        await StartAsync();

        Assert.Equal(["All categories"], Screen.Categories.Select(category => category.Name));
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task CategoriesThatFailUnexpectedlyAreLogged()
    {
        _harness.Catalog.Refusal = new InvalidOperationException("Something broke.");

        await StartAsync();

        Assert.Equal(["All categories"], Screen.Categories.Select(category => category.Name));
        Assert.StartsWith(
            "Warning: Reading the categories of game skyrimspecialedition from nexus failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesWithTheCategoryTimeWindowAndPageSizeChosen()
    {
        SkyrimCategories("Armour");
        _harness.Searchable(BrowseHarness.Skyrim, 120);
        await StartAsync();

        Screen.SelectedCategory = Screen.Categories[1];
        await Screen.Listing;
        Screen.SelectedTimeWindow = Screen.TimeWindows[2];
        await Screen.Listing;
        Screen.SelectedPageSize = Screen.PageSizes[1];
        await Screen.Listing;

        SearchQuery last = _harness.Lookup.Searches[^1].Query;
        Assert.Equal(("Armour", TestData.Now.AddDays(-7), 50, 0), (last.Category, last.UpdatedSince, last.Count, last.Offset));
        Assert.Equal(50, Screen.Results.Count);
        Assert.False(Screen.IsShowingRows);
    }

    [Fact]
    public async Task LoadMoreKeepsTheCategoryAndTheMomentTheWindowStarts()
    {
        SkyrimCategories("Armour");
        _harness.Searchable(BrowseHarness.Skyrim, 45);
        await StartAsync();
        Screen.SelectedCategory = Screen.Categories[1];
        await Screen.Listing;
        Screen.SelectedTimeWindow = Screen.TimeWindows[1];
        await Screen.Listing;

        _harness.Time.Advance(TimeSpan.FromHours(2));
        await Screen.LoadMoreCommand.ExecuteAsync(null);

        SearchQuery next = _harness.Lookup.Searches[^1].Query;
        Assert.Equal(("Armour", TestData.Now.AddDays(-1), 20, 20), (next.Category, next.UpdatedSince, next.Count, next.Offset));
    }

    [Fact]
    public async Task ChoosingEveryCategoryAndAnyTimeSearchesWithNeither()
    {
        SkyrimCategories("Armour");
        await StartAsync();
        Screen.SelectedCategory = Screen.Categories[1];
        Screen.SelectedTimeWindow = Screen.TimeWindows[3];

        Screen.SelectedCategory = null;
        Screen.SelectedTimeWindow = Screen.TimeWindows[0];
        await Screen.Listing;

        SearchQuery last = _harness.Lookup.Searches[^1].Query;
        Assert.Equal(((string?)null, (DateTimeOffset?)null), (last.Category, last.UpdatedSince));
    }

    [Fact]
    public async Task ChoicesChangedBeforeAGameSearchNothing()
    {
        await _harness.StartAsync();

        Screen.SelectedTimeWindow = Screen.TimeWindows[1];
        Screen.SelectedPageSize = Screen.PageSizes[2];
        Screen.SelectedCategory = null;

        Assert.Empty(_harness.Lookup.Searches);
    }

    [Fact]
    public async Task ChoicesChangedWhileTheTrackedModsShowListNothingElse()
    {
        await StartAsync();
        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        int searches = _harness.Lookup.Searches.Count;

        Screen.SelectedTimeWindow = Screen.TimeWindows[1];
        Screen.SelectedPageSize = Screen.PageSizes[2];

        Assert.True(Screen.IsShowingTracked);
        Assert.Equal(searches, _harness.Lookup.Searches.Count);
    }

    [Fact]
    public async Task TheTrackedModsAreListedAsManyAPageAsChosen()
    {
        await StartAsync();
        foreach (int id in Enumerable.Range(1, 60))
        {
            ModRef mod = new(BrowseHarness.Skyrim.Game, $"{id}");
            _harness.Watchlists.Tracked.Add(mod);
            _harness.Catalog.Mods[mod.Id] = BrowseHarness.Info(mod, $"Mod {id}");
        }

        Screen.SelectedPageSize = Screen.PageSizes[1];
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal(50, Screen.Results.Count);
        Assert.True(Screen.HasMore);
    }

    [Theory]
    [InlineData(SearchSort.Size, SearchDirection.Descending, "3.00 MB")]
    [InlineData(SearchSort.Size, SearchDirection.Ascending, "3.00 MB")]
    [InlineData(SearchSort.Newest, SearchDirection.Descending, "Added 5 days ago")]
    [InlineData(SearchSort.Updated, SearchDirection.Ascending, "Updated 3 days ago")]
    [InlineData(SearchSort.Downloads, SearchDirection.Descending, "")]
    public async Task EachModSaysWhatFitsTheOrder(SearchSort sort, SearchDirection direction, string fact)
    {
        ModRef mod = new(BrowseHarness.Skyrim.Game, "1");
        ModInfo info = BrowseHarness.Info(mod, "SkyUI");
        _harness.Lookup.Mods[BrowseHarness.Skyrim.Game] =
        [
            new(mod, info.Name, updatedAt: info.UpdatedAt, size: 3 * 1024 * 1024, publishedAt: TestData.Now.AddDays(-5)),
            new(new ModRef(BrowseHarness.Skyrim.Game, "2"), "Bare"),
        ];
        await StartAsync();

        Screen.SelectedSort = Assert.Single(Screen.Sorts, choice => choice.Sort == sort && choice.Direction == direction);
        await Screen.Listing;

        Assert.Equal([fact, string.Empty], Screen.Results.Select(row => row.FactText));
    }

    private void SkyrimCategories(params string[] categories)
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameCategories["skyrimspecialedition"] = categories;
    }

    /// <summary>
    /// Starts the screen, shows it, and chooses Skyrim Special Edition, which shows its rows.
    /// </summary>
    private async Task StartAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }
}
