using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse's filter card: it opens on what's applied, applies its choices together with Apply, leaves them with Close,
/// and goes back to how the game starts with Reset. A chip under the search per filter applied, removed at once with its
/// ×, and each game's filters kept while the app runs.
/// </summary>
public sealed class BrowseFiltersTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    private BrowseFiltersViewModel Card => Screen.Filters;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TheCardOpensOnWhatsAppliedOfferingWhatTheSiteSearchesBy()
    {
        await StartAsync();
        Screen.SelectedSort = Screen.Sorts[3];
        Screen.SelectedCategory = Screen.Categories[1];
        Screen.Author = "mireth";

        Screen.ToggleFiltersCommand.Execute(null);

        Assert.True(Card.IsOpen);
        Assert.Same(Screen.Sorts, Card.Sorts);
        Assert.Same(Screen.TimeWindows, Card.TimeWindows);
        Assert.Same(Screen.PageSizes, Card.PageSizes);
        Assert.Same(Screen.Categories, Card.Categories);
        Assert.Equal((Screen.Sorts[3], Screen.TimeWindows[0], Screen.Categories[1], "mireth", false, Screen.PageSizes[0]), CardChoices());
        Assert.Equal((true, true, true, true), (Card.CanSearchByUpdated, Card.CanSearchByCategory, Card.CanSearchByAuthor, Card.CanIncludeAdult));
        Assert.Equal(["Any time", "24 hours", "Week", "Month"], Card.TimeWindows.Select(window => window.ShortName));
    }

    [Fact]
    public async Task TheCardOffersOnlyWhatTheGamesSiteSearchesBy()
    {
        using BrowseHarness harness = new(example: new ExampleSite());
        await harness.StartAsync();
        await harness.ShowAsync();
        await harness.ChooseAsync(ExampleSite.StardewValley);

        harness.Screen.ToggleFiltersCommand.Execute(null);

        BrowseFiltersViewModel card = harness.Screen.Filters;
        Assert.Equal(
            (harness.Screen.CanSearchByUpdated, harness.Screen.CanSearchByCategory, harness.Screen.CanSearchByAuthor, harness.Screen.CanIncludeAdult),
            (card.CanSearchByUpdated, card.CanSearchByCategory, card.CanSearchByAuthor, card.CanIncludeAdult));
        Assert.Equal((true, false, false, false), (card.CanSearchByUpdated, card.CanSearchByCategory, card.CanSearchByAuthor, card.CanIncludeAdult));
        Assert.Same(harness.Screen.Sorts, card.Sorts);
        Assert.Equal(harness.Screen.PageSizes.Select(size => size.Count), card.PageSizes.Select(size => size.Count));
    }

    [Fact]
    public async Task WhatsChangedInTheCardSearchesOnceItsApplied()
    {
        await StartAsync();
        int searches = _harness.Lookup.Searches.Count;
        Screen.ToggleFiltersCommand.Execute(null);

        Card.SelectedSort = Screen.Sorts[3];
        Card.SelectedTimeWindow = Screen.TimeWindows[2];
        Card.SelectedCategory = Screen.Categories[1];
        Card.Author = "  mireth ";
        Card.IncludeAdultContent = true;
        Card.SelectedPageSize = Screen.PageSizes[1];

        Assert.Equal(searches, _harness.Lookup.Searches.Count);
        Assert.Equal(Screen.Sorts[0], Screen.SelectedSort);

        Card.ApplyCommand.Execute(null);
        await Screen.Listing;

        Assert.False(Card.IsOpen);
        SearchQuery query = Assert.Single(_harness.Lookup.Searches.Skip(searches)).Query;
        Assert.Equal(
            (Screen.Sorts[3].Sort, Screen.Sorts[3].Direction, TestData.Now.AddDays(-7), "Armour", "mireth", true, 50),
            (query.Sort, query.Direction, query.UpdatedSince, query.Category, query.Author, query.IncludeAdultContent, query.Count));
        Assert.False(Screen.IsShowingRows);
        Assert.Equal("mireth", Screen.Author);
    }

    [Fact]
    public async Task CloseLeavesWhatWasApplied()
    {
        await StartAsync();
        int searches = _harness.Lookup.Searches.Count;
        Screen.ToggleFiltersCommand.Execute(null);
        Card.SelectedCategory = Screen.Categories[1];
        Card.Author = "mireth";

        Card.CloseCommand.Execute(null);

        Assert.False(Card.IsOpen);
        Assert.Equal((Screen.Categories[0], string.Empty), (Screen.SelectedCategory, Screen.Author));
        Assert.Equal(searches, _harness.Lookup.Searches.Count);

        Screen.ToggleFiltersCommand.Execute(null);

        Assert.Equal((Screen.Categories[0], string.Empty), (Card.SelectedCategory, Card.Author));
    }

    [Fact]
    public async Task TheFiltersButtonClosesAnOpenCard()
    {
        await StartAsync();
        Screen.ToggleFiltersCommand.Execute(null);
        Card.Author = "mireth";

        Screen.ToggleFiltersCommand.Execute(null);

        Assert.False(Card.IsOpen);
        Assert.Equal(string.Empty, Screen.Author);
        Assert.False(Card.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingWhatsAppliedSearchesNothing()
    {
        await StartAsync();
        int searches = _harness.Lookup.Searches.Count;
        Screen.ToggleFiltersCommand.Execute(null);

        Card.ApplyCommand.Execute(null);

        Assert.Equal(searches, _harness.Lookup.Searches.Count);
        Assert.True(Screen.IsShowingRows);
    }

    [Fact]
    public async Task OnlyAdultContentAppliedWhileTheRowsShowListsTheRowsAgain()
    {
        await StartAsync();
        int searches = _harness.Lookup.Searches.Count;
        Screen.ToggleFiltersCommand.Execute(null);
        Card.IncludeAdultContent = true;

        Card.ApplyCommand.Execute(null);
        await Screen.RowsWork;

        Assert.True(Screen.IsShowingRows);
        Assert.All(_harness.Lookup.Searches.Skip(searches), search => Assert.Equal((BrowseViewModel.RowSize, true), (search.Query.Count, search.Query.IncludeAdultContent)));
        Assert.NotEmpty(_harness.Lookup.Searches.Skip(searches));
    }

    [Fact]
    public async Task ResetPutsTheCardBackToHowTheGameStartsForApplyToSearch()
    {
        _harness.Settings.Stored = SkyrimStartsOn(new BrowseDefaults { Sort = SearchSort.Updated, UpdatedWithin = TimeSpan.FromDays(30), PageSize = 50 });
        await StartAsync();
        Screen.SelectedSort = Screen.Sorts[0];
        Screen.SelectedTimeWindow = Screen.TimeWindows[1];
        Screen.SelectedCategory = Screen.Categories[1];
        Screen.Author = "mireth";
        Screen.IncludeAdultContent = true;
        Screen.SelectedPageSize = Screen.PageSizes[2];
        await Screen.Listing;
        int searches = _harness.Lookup.Searches.Count;
        Screen.ToggleFiltersCommand.Execute(null);

        await Card.ResetCommand.ExecuteAsync(null);

        Assert.Equal(("Recently updated", "Updated in the last month", "All categories", string.Empty, false, 50), CardNames());
        Assert.Equal(searches, _harness.Lookup.Searches.Count);
        Assert.Equal("mireth", Screen.Author);

        Card.ApplyCommand.Execute(null);
        await Screen.Listing;

        SearchQuery query = Assert.Single(_harness.Lookup.Searches.Skip(searches)).Query;
        Assert.Equal(
            (SearchSort.Updated, TestData.Now.AddDays(-30), (string?)null, (string?)null, false, 50),
            (query.Sort, query.UpdatedSince, query.Category, query.Author, query.IncludeAdultContent, query.Count));
    }

    [Fact]
    public async Task ResetWithoutTheGamesOwnSettingsGoesBackToBrowsesOwn()
    {
        await StartAsync();
        Screen.SelectedSort = Screen.Sorts[4];
        Screen.SelectedTimeWindow = Screen.TimeWindows[1];
        Screen.SelectedPageSize = Screen.PageSizes[2];
        Screen.ToggleFiltersCommand.Execute(null);

        await Card.ResetCommand.ExecuteAsync(null);

        Assert.Equal((Screen.Sorts[0], Screen.TimeWindows[0], Screen.Categories[0], string.Empty, false, Screen.PageSizes[0]), CardChoices());
    }

    [Fact]
    public async Task ResetWithSettingsThatCantBeReadGoesBackToBrowsesOwn()
    {
        await StartAsync();
        Screen.SelectedPageSize = Screen.PageSizes[2];
        Screen.ToggleFiltersCommand.Execute(null);
        _harness.Settings.ReadRefusal = new InvalidDataException("Not JSON.");

        await Card.ResetCommand.ExecuteAsync(null);

        Assert.Same(Screen.PageSizes[0], Card.SelectedPageSize);
        Assert.True(Card.IsOpen);
    }

    [Fact]
    public async Task EachNarrowingFilterAppliedHasAChipAndIsCountedOnTheButton()
    {
        await StartAsync();

        Assert.Equal(("Filters", false), (Screen.FiltersText, Screen.HasChips));
        Assert.Empty(Screen.Chips);

        Screen.ToggleFiltersCommand.Execute(null);
        Card.SelectedSort = Screen.Sorts[3];
        Card.SelectedPageSize = Screen.PageSizes[1];
        Card.SelectedTimeWindow = Screen.TimeWindows[2];
        Card.SelectedCategory = Screen.Categories[1];
        Card.Author = " mireth ";
        Card.IncludeAdultContent = true;
        Card.ApplyCommand.Execute(null);
        await Screen.Listing;

        Assert.Equal(
            [
                ("Updated in the last week", "Remove Updated in the last week"),
                ("Category: Armour", "Remove Category: Armour"),
                ("Author: mireth", "Remove Author: mireth"),
                ("Adult content included", "Remove Adult content included"),
            ],
            Screen.Chips.Select(chip => (chip.Label, chip.RemoveLabel)));
        Assert.Equal(("Filters · 4", true), (Screen.FiltersText, Screen.HasChips));
    }

    [Fact]
    public async Task AChipsCrossRemovesItsFilterAndSearchesAgainAtOnce()
    {
        await StartAsync();
        await ApplyAsync(card =>
        {
            card.SelectedCategory = Screen.Categories[1];
            card.Author = "mireth";
            card.SelectedPageSize = Screen.PageSizes[1];
        });
        int searches = _harness.Lookup.Searches.Count;

        Screen.Chips.Single(chip => chip.Label == "Category: Armour").RemoveCommand.Execute(null);
        await Screen.Listing;

        Assert.Equal(["Author: mireth"], Screen.Chips.Select(chip => chip.Label));
        Assert.Equal(("Filters · 1", true), (Screen.FiltersText, Screen.HasChips));
        Assert.Same(Screen.Categories[0], Screen.SelectedCategory);
        SearchQuery query = Assert.Single(_harness.Lookup.Searches.Skip(searches)).Query;
        Assert.Equal(((string?)null, "mireth", 50), (query.Category, query.Author, query.Count));
    }

    [Fact]
    public async Task EveryChipCrossTakesAwayItsOwnFilter()
    {
        await StartAsync();
        await ApplyAsync(card =>
        {
            card.SelectedTimeWindow = Screen.TimeWindows[1];
            card.Author = "mireth";
            card.IncludeAdultContent = true;
        });

        Screen.Chips[0].RemoveCommand.Execute(null);
        await Screen.Listing;

        Assert.Equal((Screen.TimeWindows[0], "mireth", true), (Screen.SelectedTimeWindow, Screen.Author, Screen.IncludeAdultContent));

        Screen.Chips[0].RemoveCommand.Execute(null);
        await Screen.Listing;

        Assert.Equal((string.Empty, true), (Screen.Author, Screen.IncludeAdultContent));
        Assert.Null(_harness.Lookup.Searches[^1].Query.Author);

        Screen.Chips[0].RemoveCommand.Execute(null);
        await Screen.Listing;

        Assert.False(Screen.IncludeAdultContent);
        Assert.False(Screen.HasChips);
        Assert.False(_harness.Lookup.Searches[^1].Query.IncludeAdultContent);
    }

    [Fact]
    public async Task ClearAllRemovesEveryFilterKeepingTheOrderAndPageSizeAndSearchesOnce()
    {
        await StartAsync();
        await ApplyAsync(card =>
        {
            card.SelectedSort = Screen.Sorts[3];
            card.SelectedTimeWindow = Screen.TimeWindows[1];
            card.SelectedCategory = Screen.Categories[1];
            card.Author = "mireth";
            card.IncludeAdultContent = true;
            card.SelectedPageSize = Screen.PageSizes[1];
        });
        int searches = _harness.Lookup.Searches.Count;

        Screen.ClearFiltersCommand.Execute(null);
        await Screen.Listing;

        Assert.Empty(Screen.Chips);
        Assert.Equal(("Filters", false), (Screen.FiltersText, Screen.HasChips));
        SearchQuery query = Assert.Single(_harness.Lookup.Searches.Skip(searches)).Query;
        Assert.Equal(
            (Screen.Sorts[3].Sort, (DateTimeOffset?)null, (string?)null, (string?)null, false, 50),
            (query.Sort, query.UpdatedSince, query.Category, query.Author, query.IncludeAdultContent, query.Count));
    }

    [Fact]
    public async Task OnlyWhatTheGamesSiteSearchesByHasAChip()
    {
        using BrowseHarness harness = new(example: new ExampleSite());
        await harness.StartAsync();
        await harness.ShowAsync();
        await harness.ChooseAsync(BrowseHarness.Skyrim);
        harness.Screen.Author = "mireth";
        harness.Screen.IncludeAdultContent = true;

        await harness.ChooseAsync(ExampleSite.StardewValley);

        Assert.Empty(harness.Screen.Chips);
        Assert.Equal("Filters", harness.Screen.FiltersText);
    }

    [Fact]
    public async Task EachGameGetsBackItsFiltersWhileTheAppRuns()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameCategories["stardewvalley"] = ["Gameplay"];
        await StartAsync();
        await ApplyAsync(card =>
        {
            card.SelectedSort = Screen.Sorts[3];
            card.SelectedTimeWindow = Screen.TimeWindows[2];
            card.SelectedCategory = Screen.Categories[1];
            card.Author = "mireth";
            card.SelectedPageSize = Screen.PageSizes[1];
        });

        await _harness.ChooseAsync(BrowseHarness.Stardew);
        await ApplyAsync(card =>
        {
            card.SelectedSort = Screen.Sorts[5];
            card.SelectedTimeWindow = Screen.TimeWindows[3];
            card.SelectedCategory = Screen.Categories[1];
            card.Author = string.Empty;
            card.IncludeAdultContent = true;
        });

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal(
            (Screen.Sorts[3], Screen.TimeWindows[2], "Armour", "mireth", false, Screen.PageSizes[1]),
            (Screen.SelectedSort, Screen.SelectedTimeWindow, Screen.SelectedCategory?.Category, Screen.Author, Screen.IncludeAdultContent, Screen.SelectedPageSize));
        Assert.Same(Screen.Categories[1], Screen.SelectedCategory);
        Assert.Equal(["Updated in the last week", "Category: Armour", "Author: mireth"], Screen.Chips.Select(chip => chip.Label));
        Assert.True(Screen.IsShowingRows);

        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.Equal(
            (Screen.Sorts[5], Screen.TimeWindows[3], "Gameplay", string.Empty, true),
            (Screen.SelectedSort, Screen.SelectedTimeWindow, Screen.SelectedCategory?.Category, Screen.Author, Screen.IncludeAdultContent));
    }

    [Fact]
    public async Task AGameChosenAgainKeepsItsFiltersOverItsOwnSettings()
    {
        _harness.Settings.Stored = SkyrimStartsOn(new BrowseDefaults { PageSize = 100 });
        await StartAsync();
        await ApplyAsync(card => card.SelectedPageSize = Screen.PageSizes[1]);
        await _harness.ChooseAsync(BrowseHarness.Stardew);
        await ApplyAsync(card => card.SelectedPageSize = Screen.PageSizes[0]);
        int searches = _harness.Lookup.Searches.Count;

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal(50, Screen.SelectedPageSize.Count);
        Assert.True(Screen.IsShowingRows);
        Assert.All(_harness.Lookup.Searches.Skip(searches), search => Assert.Equal(BrowseViewModel.RowSize, search.Query.Count));
    }

    [Fact]
    public async Task ACategoryTheSiteNoLongerHasGoesBackToEveryCategory()
    {
        await StartAsync();
        await ApplyAsync(card => card.SelectedCategory = Screen.Categories[1]);
        await _harness.ChooseAsync(BrowseHarness.Stardew);
        _harness.Catalog.GameCategories["skyrimspecialedition"] = ["Audio"];

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Same(Screen.Categories[0], Screen.SelectedCategory);
        Assert.Equal(["All categories", "Audio"], Screen.Categories.Select(category => category.Name));
        Assert.Empty(Screen.Chips);
    }

    [Fact]
    public async Task ACategoryKeptIsOfferedWhenTheSiteCantSayItsCategories()
    {
        await StartAsync();
        await ApplyAsync(card => card.SelectedCategory = Screen.Categories[1]);
        await _harness.ChooseAsync(BrowseHarness.Stardew);
        _harness.Catalog.Refusal = new HttpRequestException("Offline.");

        await _harness.ChooseAsync(BrowseHarness.Skyrim);

        Assert.Equal(["All categories", "Armour"], Screen.Categories.Select(category => category.Name));
        Assert.Same(Screen.Categories[1], Screen.SelectedCategory);
    }

    [Fact]
    public async Task ChoosingAnotherGameClosesTheCard()
    {
        await StartAsync();
        Screen.ToggleFiltersCommand.Execute(null);
        Card.Author = "mireth";

        await _harness.ChooseAsync(BrowseHarness.Stardew);

        Assert.False(Card.IsOpen);
        Assert.Equal(string.Empty, Screen.Author);
    }

    [Fact]
    public async Task TheResultsSayTheirOrder()
    {
        await StartAsync();

        Assert.Equal(string.Empty, Screen.OrderText);

        Screen.SelectedSort = Screen.Sorts.Single(sort => sort.Name == "Most downloaded");
        await Screen.Listing;

        Assert.Equal("Order: Most downloaded", Screen.OrderText);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, Screen.OrderText);
    }

    [Fact]
    public async Task ShowingTheRowsAgainSaysNoOrder()
    {
        await StartAsync();
        Screen.SearchText = "sky";
        await Screen.SearchCommand.ExecuteAsync(null);

        Screen.SearchText = string.Empty;

        Assert.True(Screen.IsShowingRows);
        Assert.Equal(string.Empty, Screen.OrderText);
    }

    private static AppSettings SkyrimStartsOn(BrowseDefaults defaults)
        => new()
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer)
            {
                [BrowseHarness.Skyrim.Game] = new GameSettings { Browse = defaults },
            },
        };

    private (SortChoice?, TimeWindowChoice?, CategoryChoice?, string, bool, PageSizeChoice?) CardChoices()
        => (Card.SelectedSort, Card.SelectedTimeWindow, Card.SelectedCategory, Card.Author, Card.IncludeAdultContent, Card.SelectedPageSize);

    private (string?, string?, string?, string, bool, int?) CardNames()
        => (Card.SelectedSort?.Name, Card.SelectedTimeWindow?.Name, Card.SelectedCategory?.Name, Card.Author, Card.IncludeAdultContent, Card.SelectedPageSize?.Count);

    /// <summary>
    /// Opens the card, changes it, applies it, and waits for the search.
    /// </summary>
    private async Task ApplyAsync(Action<BrowseFiltersViewModel> change)
    {
        Screen.ToggleFiltersCommand.Execute(null);
        change(Card);
        Card.ApplyCommand.Execute(null);
        await Screen.Listing;
        await Screen.RowsWork;
    }

    /// <summary>
    /// Starts the screen, shows it, and chooses Skyrim Special Edition, whose categories are Armour and Audio.
    /// </summary>
    private async Task StartAsync()
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameCategories["skyrimspecialedition"] = ["Armour", "Audio"];
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }
}
