using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Catalog;

public sealed class ModLookupTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    [Fact]
    public async Task AsksTheLinksSourceAboutItsMod()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await LookUpAsync(site);

        Assert.Equal(site.Game, page.Game);
        Assert.Equal(site.Mod, page.Mod);
        Assert.Equal(site.Files.Select(file => file.Reference), page.Files.Select(file => file.Reference));
        Assert.Equal(site.Changelogs, page.Changelogs);
        Assert.Equal(site.LinkedFile, page.LinkedFile);
        Assert.Equal(
            ["GetChangelogsAsync", "GetFilesAsync", "GetGameAsync", "GetModAsync", "RecognizeAsync"],
            site.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASiteSpecificMatchWinsOverACatchAll()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        ScriptedLinkRecognizer catchAll = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        ModPage page = await LookUpAsync(catchAll, site);

        Assert.Equal(site.LinkedFile, page.LinkedFile);
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => LookUpAsync(ScriptedLinkRecognizer.Never(TestData.Nexus)));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task SaysWhenTheLinksSourceHasNoCatalog()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => LookUpAsync(direct));

        Assert.Contains("isn't to a mod", refused.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "GetGameAsync", new AuthenticationFailedException("The site refused the account.") },
        { "GetModAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
        { "GetFilesAsync", new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable) },
        { "GetChangelogsAsync", new HttpRequestException("No route to host.") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSourceRefuses(string method, Exception failure)
    {
        FakeCatalogSource site = new(TestData.Nexus);
        site.Failures[method] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => LookUpAsync(site));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task SearchesThroughTheGamesSource()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        GameRef game = TestData.File(TestData.Nexus, "1").Mod.Game;
        SearchQuery query = new("SkyUI", SearchSort.Downloads, offset: 20);

        SearchPage page = await new ModLookup(new SourceRegistry([site, new FakeCatalogSource(TestData.ModIo)]))
            .SearchAsync(game, query, TestContext.Current.CancellationToken);

        Assert.Equal(site.Mod, Assert.Single(page.Mods));
        Assert.Equal((game, query), site.LastSearch);
    }

    [Fact]
    public async Task RefusesToSearchWithASourceThatCant()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.SearchAsync(
            TestData.File(TestData.Nexus, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.SearchAsync(
            TestData.File(TestData.ModIo, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PassesOnWhatTheSearchRefuses()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("Field 'nameStemmed' is not defined.");
        site.Failures["SearchAsync"] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => new ModLookup(new SourceRegistry([site]))
            .SearchAsync(TestData.File(TestData.Nexus, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task LooksAModUpByItsReferenceWithoutALinkedFile()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await new ModLookup(new SourceRegistry([site])).LookUpAsync(site.Mod.Mod, TestContext.Current.CancellationToken);

        Assert.Equal((site.Game, site.Mod, site.Changelogs), (page.Game, page.Mod, page.Changelogs));
        Assert.Equal(site.Files.Select(file => file.Reference), page.Files.Select(file => file.Reference));
        Assert.Null(page.LinkedFile);
        Assert.Equal(["GetChangelogsAsync", "GetFilesAsync", "GetGameAsync", "GetModAsync"], site.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RefusesToLookUpAModByReferenceWhoseSourceCantDescribeIt()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.LookUpAsync(TestData.File(TestData.Nexus, "1").Mod, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.LookUpAsync(TestData.File(TestData.ModIo, "1").Mod, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LookingUpByReferencePassesOnWhatTheSourceRefuses()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("No Mod Found", null, HttpStatusCode.NotFound);
        site.Failures["GetModAsync"] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => new ModLookup(new SourceRegistry([site])).LookUpAsync(site.Mod.Mod, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task ReadsEachSourcesGamesOnceAndKeepsThem()
    {
        FakeCatalogSource nexus = new(TestData.Nexus);
        FakeCatalogSource modIo = new(TestData.ModIo);
        ModLookup lookup = new(new SourceRegistry([nexus, modIo]));

        IReadOnlyList<GameInfo> first = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> again = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> other = await lookup.GetGamesAsync(TestData.ModIo, TestContext.Current.CancellationToken);

        Assert.Equal([nexus.Game], first);
        Assert.Same(first, again);
        Assert.Equal([modIo.Game], other);
        Assert.Equal(["GetGamesAsync"], nexus.Asked);
        Assert.Equal(["GetGamesAsync"], modIo.Asked);
    }

    [Fact]
    public async Task ReadsTheGamesAgainAfterAFailedRead()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        site.Failures["GetGamesAsync"] = failure;
        ModLookup lookup = new(new SourceRegistry([site]));

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken));
        IReadOnlyList<GameInfo> games = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);

        Assert.Same(failure, thrown);
        Assert.Equal([site.Game], games);
        Assert.Equal(["GetGamesAsync", "GetGamesAsync"], site.Asked);
    }

    [Fact]
    public async Task RefusesTheGamesOfASourceThatCantDescribeThem()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.GetGamesAsync(TestData.ModIo, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ListsTheSourcesThatCanSearch()
    {
        ModLookup lookup = new(new SourceRegistry(
            [
                new FakeCatalogSource(TestData.Nexus),
                ScriptedLinkRecognizer.Never(new SourceId("direct")),
                new CatalogOnlySource(new SourceId("catalog-only")),
                new FakeCatalogSource(TestData.ModIo),
            ]));

        Assert.Equal([TestData.Nexus, TestData.ModIo], lookup.SearchableSources);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        ModLookup lookup = new(new SourceRegistry([new FakeCatalogSource(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookUpAsync(
            new Uri("skyrimspecialedition/mods/12604", UriKind.Relative), TestContext.Current.CancellationToken));
    }

    private static Task<ModPage> LookUpAsync(params IModSource[] sources)
        => new ModLookup(new SourceRegistry(sources)).LookUpAsync(Link, TestContext.Current.CancellationToken);
}
