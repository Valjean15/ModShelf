using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

public sealed class DegModsSourceSearchTests : IDisposable
{
    private static readonly GameRef TestGame = new(new SourceId("degmods"), RecordedDegMods.TestGame.ToLowerInvariant());

    private readonly RecordedDegMods _degMods = new();
    private readonly ServiceProvider _services;

    public DegModsSourceSearchTests()
    {
        _degMods.AddTestGame();
        _services = _degMods.BuildServices();
    }

    [Fact]
    public void ListsNewestFirstAndCanSearchByWordsAuthorAndTimeLeavingOutAdultMods()
    {
        SearchCapabilities can = Source().Capabilities;

        Assert.Equal(new SearchOrder(SearchSort.Newest, SearchDirection.Descending), can.Orders[0]);
        Assert.Equal(5, can.Orders.Count);
        Assert.True(can.Has(SearchSort.Name));
        Assert.False(can.Has(SearchSort.Downloads));
        Assert.Equal(SearchFilters.Text | SearchFilters.Author | SearchFilters.UpdatedSince | SearchFilters.AdultContent, can.Filters);
        Assert.True(can.MarksAdultContent);
        Assert.Equal(50, can.MaxCount);
    }

    [Fact]
    public async Task ListsAGamesModsNewestFirstAPageAtATimeWithoutAdultMods()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        SearchPage first = await Source().SearchAsync(TestGame, new SearchQuery(count: 20), cancellationToken);
        SearchPage last = await Source().SearchAsync(TestGame, new SearchQuery(offset: 40, count: 20), cancellationToken);

        int shown = RecordedDegMods.TestGameMods - RecordedDegMods.TestGameAdultMods;
        Assert.Equal((shown, 20, 0), (first.TotalCount, first.Mods.Count, first.LeftOut));
        Assert.Equal("Even Test Mod 58", first.Mods[0].Name);
        Assert.Equal(shown - 40, last.Mods.Count);
        Assert.False(last.HasMore);
        Assert.All(first.Mods.Concat(last.Mods), mod => Assert.False(mod.HasAdultContent));
    }

    [Fact]
    public async Task IncludesAdultModsWhenAsked()
    {
        SearchPage page = await Source().SearchAsync(TestGame, new SearchQuery(count: 50, includeAdultContent: true), TestContext.Current.CancellationToken);

        Assert.Equal(RecordedDegMods.TestGameMods, page.TotalCount);
        Assert.Equal(5, page.Mods.Count(mod => mod.HasAdultContent));
    }

    [Theory]
    [InlineData(SearchSort.Newest, SearchDirection.Descending, "Odd Test Mod 59")]
    [InlineData(SearchSort.Newest, SearchDirection.Ascending, "Even Test Mod 00")]
    [InlineData(SearchSort.Updated, SearchDirection.Descending, "Odd Test Mod 59")]
    [InlineData(SearchSort.Name, SearchDirection.Ascending, "Even Test Mod 00")]
    [InlineData(SearchSort.Name, SearchDirection.Descending, "Odd Test Mod 59")]
    [InlineData(SearchSort.Relevance, SearchDirection.Descending, "Odd Test Mod 59")]
    public async Task ListsInEachOrder(SearchSort sort, SearchDirection direction, string firstName)
    {
        SearchPage page = await Source().SearchAsync(TestGame, new SearchQuery(sort: sort, direction: direction, count: 50, includeAdultContent: true), TestContext.Current.CancellationToken);

        Assert.Equal(firstName, page.Mods[0].Name);
    }

    [Fact]
    public async Task ListsRecentlyChangedByWhenTheirEventsWereMadeAndNewestByWhenTheyWerePublished()
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("old", "Published first, changed last", "Changing", 100, [], ["published_at", "10"]));
        _degMods.Brs.Add(author.Mod("new", "Published last", "Changing", 50, []));
        GameRef changing = new(new SourceId("degmods"), "changing");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        SearchPage updated = await Source().SearchAsync(changing, new SearchQuery(sort: SearchSort.Updated), cancellationToken);
        SearchPage newest = await Source().SearchAsync(changing, new SearchQuery(sort: SearchSort.Newest), cancellationToken);

        Assert.Equal(["Published first, changed last", "Published last"], updated.Mods.Select(mod => mod.Name));
        Assert.Equal(["Published last", "Published first, changed last"], newest.Mods.Select(mod => mod.Name));
    }

    [Fact]
    public async Task ListsByNameWhateverTheCase()
    {
        TestAuthor author = new(4);
        _degMods.Brs.Add(author.Mod("b", "beta", "Cased", 100, []));
        _degMods.Brs.Add(author.Mod("a", "Alpha", "Cased", 200, []));
        _degMods.Brs.Add(author.Mod("c", "CHARLIE", "Cased", 300, []));

        SearchPage page = await Source().SearchAsync(
            new GameRef(new SourceId("degmods"), "cased"), new SearchQuery(sort: SearchSort.Name, direction: SearchDirection.Ascending), TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "beta", "CHARLIE"], page.Mods.Select(mod => mod.Name));
    }

    [Theory]
    [InlineData("odd 5", 9)]
    [InlineData("EVEN mod", 30)]
    [InlineData("nothing like it", 0)]
    public async Task FindsTheModsWithEveryWordInTheirNames(string words, int count)
    {
        SearchPage page = await Source().SearchAsync(TestGame, new SearchQuery(words, count: 50), TestContext.Current.CancellationToken);

        Assert.Equal(count, page.TotalCount);
        Assert.All(page.Mods, mod => Assert.All(words.Split(' '), word => Assert.Contains(word, mod.Name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task FindsAnAuthorsModsByTheirNpubOrKey()
    {
        TestAuthor other = new(4);
        _degMods.Brs.Add(other.Mod("other", "Other's mod", RecordedDegMods.TestGame, 100, []));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        SearchPage byNpub = await Source().SearchAsync(TestGame, new SearchQuery(author: other.Npub), cancellationToken);
        SearchPage byKey = await Source().SearchAsync(TestGame, new SearchQuery(author: other.PublicKey.ToUpperInvariant()), cancellationToken);

        Assert.Equal("Other's mod", Assert.Single(byNpub.Mods).Name);
        Assert.Equal(other.Npub, byNpub.Mods[0].Author);
        Assert.Equal(byNpub.Mods.Select(mod => mod.Mod), byKey.Mods.Select(mod => mod.Mod));
    }

    [Fact]
    public async Task RefusesAnAuthorThatIsntAKey()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Source().SearchAsync(TestGame, new SearchQuery(author: "Smook"), TestContext.Current.CancellationToken));

        Assert.Contains("npub1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindsTheModsChangedSinceATime()
    {
        DateTimeOffset since = RecordedDegMods.RecordedAt.AddDays(-10);

        SearchPage page = await Source().SearchAsync(TestGame, new SearchQuery(count: 50, updatedSince: since, includeAdultContent: true), TestContext.Current.CancellationToken);

        // Test mods 51 to 59.
        Assert.Equal(9, page.TotalCount);
        Assert.All(page.Mods, mod => Assert.True(mod.UpdatedAt >= since));
    }

    [Fact]
    public async Task RefusesACategoryOrAnOrderItHasnt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => Source().SearchAsync(TestGame, new SearchQuery(category: "Music"), cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().SearchAsync(TestGame, new SearchQuery(sort: SearchSort.Downloads), cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().SearchAsync(TestGame, new SearchQuery(sort: SearchSort.Updated, direction: SearchDirection.Ascending), cancellationToken));
    }

    [Fact]
    public async Task AGameItShowsNoModsOfHasNone()
    {
        SearchPage page = await Source().SearchAsync(new GameRef(new SourceId("degmods"), "nothing"), new SearchQuery(), TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (page.TotalCount, page.Mods.Count));
    }

    [Fact]
    public async Task ItsModsAreDescribedAsTheCatalogDescribesThem()
    {
        DegModsSource source = Source();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ModInfo found = (await source.SearchAsync(TestGame, new SearchQuery(count: 1), cancellationToken)).Mods[0];

        ModInfo described = await source.GetModAsync(found.Mod, cancellationToken);

        Assert.Equal(described, found);
    }

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    private DegModsSource Source() => _services.GetServices<IModSource>().OfType<DegModsSource>().Single();
}
