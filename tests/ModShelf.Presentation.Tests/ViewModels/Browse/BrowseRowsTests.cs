using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// A game's rows before a search: its trending, newest and recently updated mods.
/// </summary>
public sealed class BrowseRowsTests : IDisposable
{
    private static readonly GameRef Skyrim = BrowseHarness.Skyrim.Game;

    private BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheGamesTrendingNewestAndRecentlyUpdatedMods()
    {
        _harness.Trending.Mods[Skyrim] = [Mod("12604", "SkyUI"), Mod("266", "Unofficial Patch")];
        _harness.Lookup.Mods[Skyrim] = [Mod("1", "Newest", publishedDaysAgo: 2)];

        await StartAsync();

        Assert.Equal(
            [("Trending", false), ("Recently added", true), ("Recently updated", true)],
            Screen.Rows.Select(row => (row.Title, row.CanShowAll)));
        Assert.Equal([BrowseRowKind.Trending, BrowseRowKind.RecentlyAdded, BrowseRowKind.RecentlyUpdated], Screen.Rows.Select(row => row.Kind));
        Assert.Equal(["SkyUI", "Unofficial Patch"], Names(0));
        Assert.Equal([string.Empty, string.Empty], Screen.Rows[0].Mods.Select(mod => mod.FactText));
        Assert.Equal(("Newest", "Added 2 days ago"), (Names(1)[0], Screen.Rows[1].Mods[0].FactText));
        Assert.Equal(("Newest", "Updated 3 days ago"), (Names(2)[0], Screen.Rows[2].Mods[0].FactText));
        Assert.All(Screen.Rows, row => Assert.Equal((true, false, null, false), (row.IsShown, row.IsLoading, row.Problem, row.IsEmpty)));
        Assert.Equal([Skyrim], _harness.Trending.Asked);
        Assert.Equal(
            [(SearchSort.Newest, BrowseViewModel.RowSize, false), (SearchSort.Updated, BrowseViewModel.RowSize, false)],
            _harness.Lookup.Searches.Select(search => (search.Query.Sort, search.Query.Count, search.Query.IncludeAdultContent)).OrderByDescending(search => search.Sort));
    }

    [Fact]
    public async Task ARowListsTenModsAtMost()
    {
        _harness.Trending.Mods[Skyrim] = [.. Enumerable.Range(1, 12).Select(id => Mod($"{id}", $"Mod {id}"))];

        await StartAsync();

        Assert.Equal(BrowseViewModel.RowSize, Screen.Rows[0].Mods.Count);
    }

    [Fact]
    public async Task ARowWithoutModsSaysSo()
    {
        await StartAsync();

        Assert.All(Screen.Rows, row => Assert.True(row.IsEmpty));
    }

    [Fact]
    public async Task AdultModsShowOnlyWhenAskedForAndAskingListsTheRowsAgain()
    {
        _harness.Trending.Mods[Skyrim] = [Mod("1", "Clean"), Mod("2", "Adult", adult: true)];
        await StartAsync();
        string[] before = Names(0);

        Screen.IncludeAdultContent = true;
        await Screen.RowsWork;

        Assert.Equal(["Clean"], before);
        Assert.Equal(["Clean", "Adult"], Names(0));
        Assert.True(Screen.IsShowingRows);
        Assert.Equal([Skyrim, Skyrim], _harness.Trending.Asked);
        Assert.Equal([false, false, true, true], _harness.Lookup.Searches.Select(search => search.Query.IncludeAdultContent));
    }

    [Fact]
    public async Task ASiteWithoutTrendingModsHasNoTrendingRow()
    {
        _harness.Dispose();
        _harness = new BrowseHarness(trending: false);
        _harness.Lookup.Mods[Skyrim] = [Mod("1", "Newest")];

        await StartAsync();

        Assert.Equal([false, true, true], Screen.Rows.Select(row => row.IsShown));
        Assert.Equal(["Newest"], Names(1));
    }

    [Fact]
    public async Task ARowTheSiteRefusesSaysWhyTheOthersShowAndShowingTheRowsAgainTriesAgain()
    {
        _harness.Trending.Refusal = new HttpRequestException("Service unavailable.");
        _harness.Lookup.Mods[Skyrim] = [Mod("1", "Newest")];
        await StartAsync();
        (string?, bool) refused = (Screen.Rows[0].Problem, Screen.Rows[0].IsEmpty);
        string[] newest = Names(1);

        _harness.Trending.Refusal = null;
        _harness.Trending.Mods[Skyrim] = [Mod("12604", "SkyUI")];
        Screen.SearchText = "sky";
        await Screen.SearchCommand.ExecuteAsync(null);
        Screen.SearchText = string.Empty;
        await Screen.RowsWork;

        Assert.Equal(("Nexus Mods couldn't list them: Service unavailable.", false), refused);
        Assert.Equal(["Newest"], newest);
        Assert.Null(Screen.Problem);
        Assert.Equal(["SkyUI"], Names(0));
        Assert.Null(Screen.Rows[0].Problem);
    }

    [Fact]
    public async Task RowsThatNeedAnAccountLeadToTheAccountsScreen()
    {
        _harness.Trending.Refusal = new AuthenticationFailedException();

        await StartAsync();

        Assert.True(Screen.NeedsAccount);
        Assert.StartsWith("Nexus Mods needs your account linked", Screen.Problem, StringComparison.Ordinal);
        Assert.Equal(Screen.Problem, Screen.Rows[0].Problem);
    }

    [Fact]
    public async Task ARowThatFailsUnexpectedlyIsLogged()
    {
        _harness.Trending.Refusal = new InvalidOperationException("Something broke.");

        await StartAsync();

        Assert.Equal("Something went wrong. The log has the details.", Screen.Rows[0].Problem);
        Assert.StartsWith("Error: Listing the Trending row failed System.InvalidOperationException", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearingTheWordsShowsTheRowsAgainWithoutListingThemAgain()
    {
        _harness.Trending.Mods[Skyrim] = [Mod("12604", "SkyUI")];
        _harness.Searchable(BrowseHarness.Skyrim, 3);
        await StartAsync();
        Screen.SearchText = "mod 2";
        await Screen.SearchCommand.ExecuteAsync(null);
        (bool, int) searched = (Screen.IsShowingRows, Screen.Results.Count);

        Screen.SearchText = "  ";

        Assert.Equal((false, 1), searched);
        Assert.True(Screen.IsShowingRows);
        Assert.Empty(Screen.Results);
        Assert.Equal((string.Empty, false), (Screen.ResultsText, Screen.HasMore));
        Assert.Equal(["SkyUI"], Names(0));
        Assert.Single(_harness.Trending.Asked);
    }

    [Fact]
    public async Task ChangingTheWordsWithoutClearingThemKeepsTheList()
    {
        _harness.Searchable(BrowseHarness.Skyrim, 3);
        await StartAsync();
        Screen.SearchText = "mod";
        await Screen.SearchCommand.ExecuteAsync(null);

        Screen.SearchText = "mod 1";

        Assert.False(Screen.IsShowingRows);
        Assert.Equal(3, Screen.Results.Count);
    }

    [Fact]
    public async Task ClearingTheWordsDropsTheSearchUnderWay()
    {
        _harness.Searchable(BrowseHarness.Skyrim, 3);
        await StartAsync();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Lookup.HeldSearches.Enqueue(held);
        Screen.SearchText = "mod";
        Task search = Screen.SearchCommand.ExecuteAsync(null);

        Screen.SearchText = string.Empty;
        held.SetResult();
        await search;

        Assert.True(Screen.IsShowingRows);
        Assert.Empty(Screen.Results);
        Assert.False(Screen.IsBusy);
    }

    [Theory]
    [InlineData(1, SearchSort.Newest)]
    [InlineData(2, SearchSort.Updated)]
    public async Task ShowAllListsEveryModInTheRowsOrder(int row, SearchSort sort)
    {
        _harness.Searchable(BrowseHarness.Skyrim, 30);
        await StartAsync();
        int searches = _harness.Lookup.Searches.Count;

        await Screen.Rows[row].ShowAllCommand.ExecuteAsync(null);

        (GameRef game, SearchQuery query) = Assert.Single(_harness.Lookup.Searches.Skip(searches));
        Assert.Equal((Skyrim, sort, SearchDirection.Descending, 0, BrowseViewModel.PageSize), (game, query.Sort, query.Direction, query.Offset, query.Count));
        Assert.Equal((sort, SearchDirection.Descending), (Screen.SelectedSort.Sort, Screen.SelectedSort.Direction));
        Assert.False(Screen.IsShowingRows);
        Assert.Equal(20, Screen.Results.Count);
        Assert.False(Screen.Rows[0].ShowAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnotherGameChosenWhileTheRowsAreListedReplacesThem()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Trending.Hold = held;
        _harness.Trending.Mods[Skyrim] = [Mod("12604", "SkyUI")];
        _harness.Trending.Mods[BrowseHarness.Stardew.Game] = [BrowseHarness.Info(new ModRef(BrowseHarness.Stardew.Game, "2400"), "SMAPI")];
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.Game = Screen.Games.Single(choice => choice.Game == Skyrim);
        Task skyrimRows = Screen.RowsWork;

        _harness.Trending.Hold = null;
        await _harness.ChooseAsync(BrowseHarness.Stardew);
        held.SetResult();
        await skyrimRows;

        Assert.Equal(["SMAPI"], Names(0));
        Assert.False(Screen.Rows[0].IsLoading);
        Assert.Equal([Skyrim, BrowseHarness.Stardew.Game], _harness.Trending.Asked);
    }

    [Fact]
    public async Task OpeningARowsModAndGoingBackShowsTheRows()
    {
        ModRef skyUi = new(Skyrim, "12604");
        _harness.Trending.Mods[Skyrim] = [BrowseHarness.Info(skyUi, "SkyUI")];
        _harness.Lookup.Pages[skyUi] = BrowseHarness.Page(BrowseHarness.Info(skyUi, "SkyUI"));
        await StartAsync();

        await Screen.Rows[0].Mods[0].OpenCommand.ExecuteAsync(null);
        string? opened = Screen.Mod?.Name;
        Screen.BackCommand.Execute(null);

        Assert.Equal("SkyUI", opened);
        Assert.True(Screen.IsShowingRows);
        Assert.Equal(["SkyUI"], Names(0));
    }

    [Fact]
    public async Task TheTrackedModsReplaceTheRows()
    {
        await StartAsync();

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.False(Screen.IsShowingRows);
        Assert.True(Screen.IsShowingTracked);
    }

    [Fact]
    public async Task ARowsModsShowWhatTheLibraryHasOfThemAndFollowItsChanges()
    {
        ModRef skyUi = new(Skyrim, "12604");
        _harness.Trending.Mods[Skyrim] = [BrowseHarness.Info(skyUi, "SkyUI")];
        await StartAsync();
        bool before = Screen.Rows[0].Mods[0].IsWatched;

        _harness.Library.Change(skyUi, TestData.LibraryMod(skyUi, "SkyUI"));

        Assert.False(before);
        Assert.True(Screen.Rows[0].Mods[0].IsWatched);
    }

    private static ModInfo Mod(string id, string name, bool adult = false, int publishedDaysAgo = 9)
    {
        ModInfo info = BrowseHarness.Info(new ModRef(Skyrim, id), name);
        return new ModInfo(
            info.Mod,
            name,
            pageUrl: info.PageUrl,
            updatedAt: info.UpdatedAt,
            hasAdultContent: adult,
            endorsements: 1,
            publishedAt: TestData.Now.AddDays(-publishedDaysAgo));
    }

    private string[] Names(int row) => [.. Screen.Rows[row].Mods.Select(mod => mod.Name)];

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
