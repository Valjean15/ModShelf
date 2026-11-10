using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// A game's mods, 50 a page in GameBanana's orders, narrowed by category or author; and searches by words, which
/// GameBanana answers 15 a page.
/// </summary>
public sealed class GameBananaSourceSearchTests : IDisposable
{
    private readonly GameBananaHarness _harness = new();

    public GameBananaSourceSearchTests()
    {
        _harness.Api.ServeAll();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void SearchesInItsOrdersBestMatchFirstByWordsAuthorOrCategoryAndMarksAdultMods()
    {
        SearchCapabilities can = _harness.Source.Capabilities;

        Assert.Equal(new SearchOrder(SearchSort.Relevance, SearchDirection.Descending), can.Orders[0]);
        Assert.Equal(8, can.Orders.Count);
        Assert.Equal((SearchFilters.Text | SearchFilters.Author | SearchFilters.Category, 50, true), (can.Filters, can.MaxCount, can.MarksAdultContent));
    }

    [Theory]
    [InlineData(SearchSort.Newest, SearchDirection.Descending, "Generic_Newest")]
    [InlineData(SearchSort.Relevance, SearchDirection.Descending, "Generic_Newest")]
    [InlineData(SearchSort.Newest, SearchDirection.Ascending, "Generic_Oldest")]
    [InlineData(SearchSort.Updated, SearchDirection.Descending, "Generic_LatestModified")]
    [InlineData(SearchSort.Endorsements, SearchDirection.Descending, "Generic_MostLiked")]
    [InlineData(SearchSort.Downloads, SearchDirection.Descending, "Generic_MostDownloaded")]
    [InlineData(SearchSort.Name, SearchDirection.Ascending, "Generic_Alphabetically")]
    [InlineData(SearchSort.Name, SearchDirection.Descending, "Generic_ReverseAlphabetically")]
    public async Task ListsAGamesModsInGameBananasOrder(SearchSort sort, SearchDirection direction, string named)
    {
        SearchPage page = await SearchAsync(new SearchQuery(sort: sort, direction: direction, count: 50));

        Assert.Equal(50, page.Mods.Count);
        Assert.Equal($"/apiv11/Mod/Index?_nPage=1&_nPerpage=50&_sSort={named}&_aFilters%5BGeneric_Game%5D=16951", Assert.Single(_harness.Asked));
    }

    [Fact]
    public async Task APageOfAGamesModsSaysWhatGameBananaDoes()
    {
        SearchPage page = await SearchAsync(new SearchQuery(count: 50));

        Assert.Equal((918, 0, true), (page.TotalCount, page.Offset, page.HasMore));
        ModInfo first = page.Mods[0];
        Assert.Equal(
            ("738438", "Workable Maid Café - Rose & Violet", "dripping-mercury", "Events", false),
            (first.Mod.Id, first.Name, first.Author, first.Category, first.HasAdultContent));
        Assert.All(page.Mods, mod => Assert.Equal(GameBananaHarness.Game, mod.Mod.Game));
    }

    [Fact]
    public async Task TheSecondPageFollowsOn()
    {
        SearchPage page = await SearchAsync(new SearchQuery(offset: 50, count: 50));

        Assert.Equal("696515", page.Mods[0].Mod.Id);
        Assert.Equal("/apiv11/Mod/Index?_nPage=2&_nPerpage=50&_sSort=Generic_Newest&_aFilters%5BGeneric_Game%5D=16951", Assert.Single(_harness.Asked));
    }

    [Fact]
    public async Task APageStartingPartWayIntoGameBananasTakesTheNextToo()
    {
        SearchPage page = await SearchAsync(new SearchQuery(offset: 25, count: 50));

        Assert.Equal(50, page.Mods.Count);
        Assert.Equal(25, page.Offset);
        Assert.Equal(
            [.. RecordedGameBananaApi.Body("mods-newest-1")["_aRecords"]!.AsArray().Skip(25).Concat(RecordedGameBananaApi.Body("mods-newest-2")["_aRecords"]!.AsArray().Take(25))
                .Select(record => record!["_idRow"]!.ToString())],
            page.Mods.Select(mod => mod.Mod.Id));
    }

    [Fact]
    public async Task NarrowsByAnAuthorFoundByName()
    {
        SearchPage page = await SearchAsync(new SearchQuery(author: "dripping-mercury", count: 50));

        Assert.Equal(5, page.Mods.Count);
        Assert.All(page.Mods, mod => Assert.Equal("dripping-mercury", mod.Author));
        Assert.Contains("_aFilters%5BGeneric_Submitter%5D=5252327", _harness.Asked[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAuthorIsTheMemberOfThatNameInAnyCase()
    {
        _harness.Api.Answer(
            "/apiv11/Util/Search/Results",
            answer => answer.WithBody("""{"_aMetadata": {"_nRecordCount": 2, "_bIsComplete": true}, "_aRecords": [{"_idRow": 1, "_sName": "Mercury!"}, {"_idRow": 5252327, "_sName": "Dripping-Mercury"}]}"""));

        SearchPage page = await SearchAsync(new SearchQuery(author: "dripping-mercury", count: 50));

        Assert.Equal(5, page.Mods.Count);
    }

    [Fact]
    public async Task AnAuthorNoMemberIsNamedHasNoMods()
    {
        _harness.Api.Answer(
            "/apiv11/Util/Search/Results",
            answer => answer.WithBody("""{"_aMetadata": {"_nRecordCount": 1, "_bIsComplete": true}, "_aRecords": [{"_idRow": 1, "_sName": "mercury"}]}"""));

        SearchPage page = await SearchAsync(new SearchQuery(author: "mercur", count: 50));

        Assert.Equal((0, 0), (page.TotalCount, page.Mods.Count));
        Assert.DoesNotContain(_harness.Asked, asked => asked.StartsWith("/apiv11/Mod/Index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrowsByATopCategoryByItsNameReadWithTheGame()
    {
        SearchPage page = await SearchAsync(new SearchQuery(category: "events", count: 50));

        Assert.Equal(44, page.Mods.Count);
        Assert.All(page.Mods, mod => Assert.Equal("Events", mod.Category));
        Assert.Equal("/apiv11/Game/16951/ProfilePage", _harness.Asked[0]);
        Assert.Contains("_aFilters%5BGeneric_Category%5D=20019", _harness.Asked[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesACategoryTheGameDoesntHave()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new SearchQuery(category: "Weapons", count: 50)));
    }

    [Fact]
    public async Task SearchesByWordsFifteenAPageAsManyPagesAsAPageTakes()
    {
        SearchPage page = await SearchAsync(new SearchQuery("Protagonist", count: 50));

        // As the first page counts them: GameBanana's count can change from one page to the next.
        int total = RecordedGameBananaApi.Body("search-1")["_aMetadata"]!["_nRecordCount"]!.GetValue<int>();
        Assert.Equal((total, 50, true), (page.TotalCount, page.Mods.Count, page.HasMore));
        Assert.Equal(
            [.. Enumerable.Range(1, 4).Select(number => $"/apiv11/Util/Search/Results?_sModelName=Mod&_sOrder=best_match&_idGameRow=16951&_sSearchString=Protagonist&_nPage={number}")],
            _harness.Asked);
        Assert.Equal("702359", page.Mods[0].Mod.Id);
    }

    [Fact]
    public async Task ASearchByWordsStartingPartWayIntoAPage()
    {
        SearchPage page = await SearchAsync(new SearchQuery("Protagonist", offset: 20, count: 10));

        Assert.Equal(10, page.Mods.Count);
        Assert.Equal(
            [.. RecordedGameBananaApi.Body("search-2")["_aRecords"]!.AsArray().Skip(5).Take(10).Select(record => record!["_idRow"]!.ToString())],
            page.Mods.Select(mod => mod.Mod.Id));
        Assert.Equal(["/apiv11/Util/Search/Results?_sModelName=Mod&_sOrder=best_match&_idGameRow=16951&_sSearchString=Protagonist&_nPage=2"], _harness.Asked);
    }

    [Theory]
    [InlineData(SearchSort.Newest, "date")]
    [InlineData(SearchSort.Updated, "udate")]
    [InlineData(SearchSort.Endorsements, "popularity")]
    [InlineData(SearchSort.Downloads, "popularity")]
    public async Task SortsASearchByWordsAsGameBananaCan(SearchSort sort, string order)
    {
        SearchPage page = await SearchAsync(new SearchQuery("Protagonist", sort, count: 15));

        Assert.Equal(RecordedGameBananaApi.Body($"search-{order}")["_aRecords"]!.AsArray().Count, page.Mods.Count);
        Assert.Contains($"_sOrder={order}&", Assert.Single(_harness.Asked), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SearchSort.Name, SearchDirection.Ascending)]
    [InlineData(SearchSort.Newest, SearchDirection.Ascending)]
    public async Task RefusesWordsInAnOrderGameBananaDoesntSortThemBy(SearchSort sort, SearchDirection direction)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new SearchQuery("Protagonist", sort, direction: direction)));
        Assert.Empty(_harness.Asked);
    }

    [Theory]
    [InlineData("Someone", null)]
    [InlineData(null, "Events")]
    public async Task RefusesWordsWithAnAuthorOrACategory(string? author, string? category)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new SearchQuery("Protagonist", author: author, category: category)));
        Assert.Empty(_harness.Asked);
    }

    [Fact]
    public async Task RefusesToSearchByWhenModsWereUpdated()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new SearchQuery(updatedSince: DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public async Task MarksTheModsGameBananaShowsBehindAWarningOrHides()
    {
        _harness.Api.Answer(
            "/apiv11/Mod/Index",
            answer => answer.WithBody("""
                {"_aMetadata": {"_nRecordCount": 3, "_bIsComplete": true}, "_aRecords": [
                    {"_idRow": 1, "_sName": "Shown", "_sInitialVisibility": "show"},
                    {"_idRow": 2, "_sName": "Warned of", "_sInitialVisibility": "warn"},
                    {"_idRow": 3, "_sName": "Hidden", "_sInitialVisibility": "hide"}]}
                """));

        SearchPage page = await SearchAsync(new SearchQuery(count: 50));

        Assert.Equal([false, true, true], page.Mods.Select(mod => mod.HasAdultContent));
    }

    [Fact]
    public async Task LeavesOutAndLogsAModGameBananaListsUnderAnotherGame()
    {
        _harness.Api.Answer(
            "/apiv11/Mod/Index",
            answer => answer.WithBody("""
                {"_aMetadata": {"_nRecordCount": 2, "_bIsComplete": true}, "_aRecords": [
                    {"_idRow": 1, "_sName": "Ours", "_aGame": {"_idRow": 16951}},
                    {"_idRow": 2, "_sName": "Another's", "_aGame": {"_idRow": 8552}}]}
                """));

        SearchPage page = await SearchAsync(new SearchQuery(count: 50));

        Assert.Equal(["1"], page.Mods.Select(mod => mod.Mod.Id));
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains("listed under game 8552", StringComparison.Ordinal));
    }

    private Task<SearchPage> SearchAsync(SearchQuery query) => _harness.Source.SearchAsync(GameBananaHarness.Game, query, GameBananaHarness.Cancellation);
}
