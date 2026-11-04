using ModShelf.Application.Catalog;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Catalog;

/// <summary>
/// A search is checked against what the site's search can do, and one asking more is refused before it's sent, saying
/// what the site can.
/// </summary>
public sealed class ModLookupSearchChecksTests
{
    private static readonly GameRef Game = TestData.File(TestData.Nexus, "1").Mod.Game;

    // Best matches, downloads either way, and names from A to Z; words and when mods were updated; 50 a page.
    private readonly FakeCatalogSource _site = new(TestData.Nexus)
    {
        Capabilities = new(
            [
                new(SearchSort.Relevance, SearchDirection.Descending),
                .. SearchOrder.BothWays(SearchSort.Downloads),
                new(SearchSort.Name, SearchDirection.Ascending),
            ],
            SearchFilters.Text | SearchFilters.UpdatedSince,
            50),
    };

    public static TheoryData<SearchQuery, string> AskingMore() => new()
    {
        { new SearchQuery(author: "Ana"), "nexus can't search by author. It can search by words and by when mods were updated." },
        { new SearchQuery(category: "Farming"), "nexus can't search by category. It can search by words and by when mods were updated." },
        {
            new SearchQuery("crops", author: "Ana", category: "Farming"),
            "nexus can't search by author or by category. It can search by words and by when mods were updated."
        },
        { new SearchQuery(sort: SearchSort.Size), "nexus can't sort by Size. It can sort by Relevance, Downloads and Name." },
        { new SearchQuery(sort: SearchSort.Name), "nexus can sort by Name only with the least first." },
        { new SearchQuery(direction: SearchDirection.Ascending), "nexus can sort by Relevance only with the most first." },
        { new SearchQuery(count: 51), "nexus lists at most 50 mods a page." },
    };

    [Theory]
    [MemberData(nameof(AskingMore))]
    public async Task ASearchAskingMoreThanTheSiteCanIsRefusedUnsent(SearchQuery query, string refusal)
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(query));

        Assert.StartsWith(refusal, refused.Message, StringComparison.Ordinal);
        Assert.Equal("query", refused.ParamName);
        Assert.Null(_site.LastSearch);
    }

    [Fact]
    public async Task ASearchTheSiteCanDoIsSent()
    {
        SearchQuery query = new(
            "crops",
            SearchSort.Downloads,
            offset: 50,
            count: 50,
            includeAdultContent: true,
            direction: SearchDirection.Ascending,
            updatedSince: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        await SearchAsync(query);

        Assert.Equal((Game, query), _site.LastSearch);
    }

    [Fact]
    public async Task IncludingAdultModsIsNoFilterASiteCanLack()
    {
        await SearchAsync(new SearchQuery(includeAdultContent: true));

        Assert.True(_site.LastSearch!.Value.Query.IncludeAdultContent);
    }

    [Fact]
    public async Task ASiteThatNarrowsByNothingSaysSo()
    {
        _site.Capabilities = new([new(SearchSort.Newest, SearchDirection.Descending)], SearchFilters.None, 20);

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new SearchQuery("crops", SearchSort.Newest)));

        Assert.StartsWith("nexus can't search by words: it lists a game's mods without narrowing them.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatItCantNarrowByIsSaidBeforeItsOrder()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => SearchAsync(new SearchQuery(sort: SearchSort.Size, count: 100, author: "Ana")));

        Assert.StartsWith("nexus can't search by author.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItsOrderIsSaidBeforeItsPageSize()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => SearchAsync(new SearchQuery(sort: SearchSort.Size, count: 100)));

        Assert.StartsWith("nexus can't sort by Size.", refused.Message, StringComparison.Ordinal);
    }

    private Task<SearchPage> SearchAsync(SearchQuery query)
        => new ModLookup(new SourceRegistry([_site])).SearchAsync(Game, query, TestContext.Current.CancellationToken);
}
