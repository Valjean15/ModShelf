using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// A game whose site's search has only some orders and filters, and smaller pages: Browse offers only those, lists only
/// the rows in the orders it has, and reads no categories it can't search by.
/// </summary>
public sealed class BrowseNarrowSearchTests : IDisposable
{
    // Its own order first: the most recently updated; then by name, either way; words only; 30 mods a page.
    private readonly BrowseHarness _harness = new(
        nexusSearch: new SearchCapabilities(
            [new(SearchSort.Updated, SearchDirection.Descending), .. SearchOrder.BothWays(SearchSort.Name)],
            SearchFilters.Text,
            30));

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OffersOnlyItsOrdersStartingOnItsOwnAndItsPageSizes()
    {
        await StartAsync();

        Assert.Equal(["Recently updated", "Name, A to Z", "Name, Z to A"], Screen.Sorts.Select(sort => sort.Name));
        Assert.Same(Screen.Sorts[0], Screen.SelectedSort);
        Assert.Equal([20], Screen.PageSizes.Select(size => size.Count));
        Assert.Equal([null], Screen.TimeWindows.Select(window => window.Within));
        Assert.Equal((false, false, false, false), (Screen.CanSearchByUpdated, Screen.CanSearchByAuthor, Screen.CanSearchByCategory, Screen.CanIncludeAdult));
    }

    [Fact]
    public async Task ListsOnlyTheRowsInOrdersItHas()
    {
        await StartAsync();

        Assert.Equal((false, true), (Screen.Rows[1].IsShown, Screen.Rows[2].IsShown));
        Assert.DoesNotContain(_harness.Lookup.Searches, search => search.Query.Sort == SearchSort.Newest);
        Assert.Contains(_harness.Lookup.Searches, search => search.Query.Sort == SearchSort.Updated);
    }

    [Fact]
    public async Task ReadsNoCategoriesItCantSearchBy()
    {
        _harness.Catalog.GameCategories["skyrimspecialedition"] = ["Armour", "Weapons"];

        await StartAsync();

        Assert.Single(Screen.Categories);
        Assert.DoesNotContain("GetGameAsync skyrimspecialedition", _harness.Catalog.Asked);
    }

    [Fact]
    public async Task SearchesAnyTimeWhateverWasChosenForAnotherGame()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.SelectedTimeWindow = Screen.TimeWindows.Single(window => window.Within == TimeSpan.FromDays(7));

        await _harness.ChooseAndSearchAsync(BrowseHarness.Skyrim);

        Assert.Null(Screen.SelectedTimeWindow.Within);
        Assert.Null(Assert.Single(_harness.Lookup.Searches).Query.UpdatedSince);
    }

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }
}
