using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// A game's mods in mod.io's orders, narrowed by words, author, tag and date, and its trending mods, as recorded.
/// </summary>
public sealed class ModIoSourceSearchTests : IDisposable
{
    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public ModIoSourceSearchTests() => _harness.Api.ServeAll();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void ListsTheNewestFirstAndNarrowsByWordsAuthorTagAndDateMarkingAdultMods()
    {
        SearchCapabilities can = _harness.Source.Capabilities;

        Assert.Equal(new SearchOrder(SearchSort.Newest, SearchDirection.Descending), can.Orders[0]);
        Assert.False(can.Has(SearchSort.Relevance));
        Assert.False(can.Has(SearchSort.Endorsements));
        Assert.Equal(SearchFilters.Text | SearchFilters.Author | SearchFilters.Category | SearchFilters.UpdatedSince, can.Filters);
        Assert.Equal((50, true), (can.MaxCount, can.MarksAdultContent));
    }

    [Theory]
    [InlineData(SearchSort.Newest, SearchDirection.Descending, "-date_live")]
    [InlineData(SearchSort.Relevance, SearchDirection.Descending, "-date_live")]
    [InlineData(SearchSort.Newest, SearchDirection.Ascending, "date_live")]
    [InlineData(SearchSort.Updated, SearchDirection.Descending, "-date_updated")]
    [InlineData(SearchSort.Downloads, SearchDirection.Descending, "-downloads_total")]
    [InlineData(SearchSort.Name, SearchDirection.Ascending, "name")]
    [InlineData(SearchSort.Name, SearchDirection.Descending, "-name")]
    public async Task AsksForTheOrderByItsColumnsName(SearchSort sort, SearchDirection direction, string column)
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(sort: sort, direction: direction, count: 50), ModIoHarness.Cancellation);

        Assert.Equal($"/v1/games/6715/mods?_sort={column}&_limit=50&_offset=0", Assert.Single(_harness.Asked));
        Assert.Equal((50, 9198, 0), (page.Mods.Count, page.TotalCount, page.Offset));
        Assert.All(page.Mods, mod => Assert.Equal(ModIoHarness.Game, mod.Mod.Game));
    }

    [Theory]
    [InlineData(SearchSort.Relevance, SearchDirection.Ascending)]
    [InlineData(SearchSort.Updated, SearchDirection.Ascending)]
    [InlineData(SearchSort.Endorsements, SearchDirection.Descending)]
    [InlineData(SearchSort.Size, SearchDirection.Descending)]
    [InlineData(SearchSort.UniqueDownloads, SearchDirection.Descending)]
    public async Task RefusesAnOrderModIoDoesntSortBy(SearchSort sort, SearchDirection direction)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(sort: sort, direction: direction), ModIoHarness.Cancellation));

        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task RefusesMoreModsThanAPageHolds()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(count: 51), ModIoHarness.Cancellation));

        Assert.Empty(_harness.Api.Requests);
    }

    [Fact]
    public async Task AsksForThePageItStartsAt()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(offset: 50, count: 50), ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods?_sort=-date_live&_limit=50&_offset=50", Assert.Single(_harness.Asked));
        Assert.Equal((50, 50, true), (page.Mods.Count, page.Offset, page.HasMore));
    }

    [Fact]
    public async Task SearchesByWords()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(" better ", count: 50), ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods?_sort=-date_live&_limit=50&_offset=0&_q=better", Assert.Single(_harness.Asked));
        Assert.Equal((50, 125), (page.Mods.Count, page.TotalCount));
    }

    [Fact]
    public async Task FindingNothingIsAnEmptyPage()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery("qqzxjvnomatchqqzxjv", count: 50), ModIoHarness.Cancellation);

        Assert.Equal((0, 0, false), (page.Mods.Count, page.TotalCount, page.HasMore));
    }

    [Fact]
    public async Task SearchesByAuthorsName()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(author: "ModioUser2js8", count: 50), ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods?_sort=-date_live&_limit=50&_offset=0&submitted_by_display_name=ModioUser2js8", Assert.Single(_harness.Asked));
        Assert.Equal(25, page.Mods.Count);
        Assert.All(page.Mods, mod => Assert.Equal("ModioUser2js8", mod.Author));
    }

    [Fact]
    public async Task SearchesByTagNamingItEachModsCategory()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(category: "Classes", count: 50), ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods?_sort=-date_live&_limit=50&_offset=0&tags=Classes", Assert.Single(_harness.Asked));
        Assert.Equal((50, 1792), (page.Mods.Count, page.TotalCount));
        Assert.All(page.Mods, mod => Assert.Equal("Classes", mod.Category));
    }

    [Fact]
    public async Task AModsCategoryIsTheTagSearchedForWhereverItIsAmongItsTags()
    {
        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(category: "english", count: 50), ModIoHarness.Cancellation);

        Assert.NotEmpty(page.Mods);
        Assert.All(page.Mods, mod => Assert.Equal("English", mod.Category));
    }

    [Fact]
    public async Task SearchesByWhenModsWereUpdatedInWholeSecondsNotBefore()
    {
        DateTimeOffset since = DateTimeOffset.FromUnixTimeSeconds(1793793600);

        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(sort: SearchSort.Updated, count: 50, updatedSince: since), ModIoHarness.Cancellation);
        await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(count: 50, updatedSince: since.AddMilliseconds(1)), ModIoHarness.Cancellation);

        Assert.Equal(
            ["/v1/games/6715/mods?_sort=-date_updated&_limit=50&_offset=0&date_updated-min=1793793600", "/v1/games/6715/mods?_sort=-date_live&_limit=50&_offset=0&date_updated-min=1793793601"],
            _harness.Asked);
        Assert.Equal((50, 186), (page.Mods.Count, page.TotalCount));
    }

    [Fact]
    public async Task NarrowsByEveryFilterAtOnce()
    {
        await _harness.Source.SearchAsync(
            ModIoHarness.Game,
            new SearchQuery("inventory ui", author: "Caites", category: "Quality of Life", count: 20, updatedSince: DateTimeOffset.FromUnixTimeSeconds(1700000000)),
            ModIoHarness.Cancellation);

        Assert.Equal(
            "/v1/games/6715/mods?_sort=-date_live&_limit=20&_offset=0&_q=inventory ui&submitted_by_display_name=Caites&tags=Quality of Life&date_updated-min=1700000000",
            Assert.Single(_harness.Asked));
        Assert.Contains("_q=inventory%20ui&submitted_by_display_name=Caites&tags=Quality%20of%20Life&", _harness.Api.Requests[0].RequestMessage!.RawQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WordsAreSentAsTheyAreWhateverTheyHold()
    {
        await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery("bags & belts #2", count: 50), ModIoHarness.Cancellation);

        Assert.EndsWith("&_q=bags%20%26%20belts%20%232", _harness.Api.Requests[0].RequestMessage!.RawQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarksModsWithExplicitContentAndLeavesNoneOut()
    {
        _harness.Api.Answer("/v1/games/6715/mods", "mods-explicit");

        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(count: 50), ModIoHarness.Cancellation);

        Assert.Equal((50, 0), (page.Mods.Count, page.LeftOut));
        Assert.All(page.Mods, mod => Assert.True(mod.HasAdultContent));
    }

    [Fact]
    public async Task LeavesOutAModOfAnotherGame()
    {
        _harness.Api.Answer("/v1/games/6715/mods", answer => answer.WithBody("""
            {"data": [{"id": 1, "game_id": 6715, "name": "Ours"}, {"id": 2, "game_id": 1, "name": "Theirs"}, {"id": 0, "name": "None"}], "result_total": 3}
            """));

        SearchPage page = await _harness.Source.SearchAsync(ModIoHarness.Game, new SearchQuery(), ModIoHarness.Cancellation);

        Assert.Equal(["1"], page.Mods.Select(mod => mod.Mod.Id));
    }

    [Fact]
    public async Task TrendingIsTheModsDownloadedMostToday()
    {
        IReadOnlyList<ModInfo> trending = await _harness.Source.GetTrendingAsync(ModIoHarness.Game, ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods?_sort=-downloads_today&_limit=50&_offset=0", Assert.Single(_harness.Asked));
        Assert.Equal(50, trending.Count);
        Assert.Equal(("5138870", "Queen's Garments"), (trending[0].Mod.Id, trending[0].Name));
    }

    [Fact]
    public async Task RefusesTrendingModsOfAGameNamedOtherThanByNumber()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Source.GetTrendingAsync(new GameRef(ModIoHarness.ModIo, "baldursgate3"), ModIoHarness.Cancellation));

        Assert.Empty(_harness.Api.Requests);
    }
}
