using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Watchlist;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Watchlist;

public sealed class WatchlistsTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FollowsTheModOfALinkOnItsSite()
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        Watchlists watchlists = new(new SourceRegistry([site]), new InMemoryLibraryRepository());

        ModRef followed = await watchlists.WatchAsync(Link, Cancellation);

        Assert.Equal(site.LinkedMod, followed);
        Assert.Equal([site.LinkedMod], await watchlists.GetWatchedAsync(TestData.Nexus, Cancellation));
        Assert.Equal(["RecognizeAsync", "WatchAsync", "GetWatchedAsync"], site.Asked);
    }

    [Fact]
    public async Task StopsFollowingTheModOfALink()
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        Watchlists watchlists = new(new SourceRegistry([site]), new InMemoryLibraryRepository());
        await watchlists.WatchAsync(Link, Cancellation);

        ModRef unfollowed = await watchlists.UnwatchAsync(Link, Cancellation);

        Assert.Equal(site.LinkedMod, unfollowed);
        Assert.Empty(await watchlists.GetWatchedAsync(TestData.Nexus, Cancellation));
    }

    [Fact]
    public async Task StopsFollowingALibraryModByItsPageWithoutAskingForTheMod()
    {
        // Such as a mod its site removed, whose links it no longer knows.
        FakeWatchlistSource site = new(TestData.Nexus);
        InMemoryLibraryRepository library = new();
        library.Add(LibraryMod.Watch(site.LinkedMod, "SkyUI", null, Link, TestData.Now));
        Watchlists watchlists = new(new SourceRegistry([site]), library);
        await watchlists.WatchAsync(Link, Cancellation);

        ModRef unfollowed = await watchlists.UnwatchAsync(new Uri("http://nexusmods.com/skyrimspecialedition/mods/12604/?tab=files"), Cancellation);

        Assert.Equal(site.LinkedMod, unfollowed);
        Assert.Equal(["RecognizeAsync", "WatchAsync", "UnwatchAsync"], site.Asked);
    }

    [Fact]
    public async Task ChangesOnlyTheWatchlistOfTheLinksSite()
    {
        FakeWatchlistSource other = new(TestData.ModIo, recognizesLinks: false);
        FakeWatchlistSource site = new(TestData.Nexus);

        await new Watchlists(new SourceRegistry([other, site]), new InMemoryLibraryRepository()).WatchAsync(Link, Cancellation);

        Assert.Equal(["RecognizeAsync"], other.Asked);
        Assert.Equal(["RecognizeAsync", "WatchAsync"], site.Asked);
    }

    [Fact]
    public async Task ASiteSpecificMatchWinsOverACatchAll()
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        ScriptedLinkRecognizer catchAll = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        ModRef followed = await new Watchlists(new SourceRegistry([catchAll, site]), new InMemoryLibraryRepository()).WatchAsync(Link, Cancellation);

        Assert.Equal(site.LinkedMod, followed);
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        Watchlists watchlists = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]), new InMemoryLibraryRepository());

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => watchlists.WatchAsync(Link, Cancellation));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task SaysWhenTheLinksSiteHasNoWatchlist()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);
        Watchlists watchlists = new(new SourceRegistry([direct]), new InMemoryLibraryRepository());

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => watchlists.UnwatchAsync(Link, Cancellation));

        Assert.Contains("can track mods", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        FakeWatchlistSource site = new(TestData.Nexus);

        await Assert.ThrowsAsync<ArgumentException>(() => new Watchlists(new SourceRegistry([site]), new InMemoryLibraryRepository())
            .WatchAsync(new Uri("skyrimspecialedition/mods/12604", UriKind.Relative), Cancellation));
        Assert.Empty(site.Asked);
    }

    public static TheoryData<string, string, Exception> Failures() => new()
    {
        { "watch", "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "watch", "WatchAsync", new AuthenticationFailedException("The site refused the account.") },
        { "watch", "WatchAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
        { "unwatch", "UnwatchAsync", new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable) },
        { "list", "GetWatchedAsync", new AuthenticationFailedException("No account is linked.") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSiteRefuses(string action, string method, Exception failure)
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        site.Failures[method] = failure;
        Watchlists watchlists = new(new SourceRegistry([site]), new InMemoryLibraryRepository());

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => action switch
        {
            "watch" => watchlists.WatchAsync(Link, Cancellation),
            "unwatch" => watchlists.UnwatchAsync(Link, Cancellation),
            _ => watchlists.GetWatchedAsync(TestData.Nexus, Cancellation),
        });

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task FollowsAndStopsFollowingAModByItsReferenceWithoutReadingALink()
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        Watchlists watchlists = new(new SourceRegistry([site]), new InMemoryLibraryRepository());

        await watchlists.WatchAsync(site.LinkedMod, Cancellation);
        IReadOnlyList<ModRef> followed = await watchlists.GetWatchedAsync(TestData.Nexus, Cancellation);
        await watchlists.UnwatchAsync(site.LinkedMod, Cancellation);

        Assert.Equal([site.LinkedMod], followed);
        Assert.Empty(await watchlists.GetWatchedAsync(TestData.Nexus, Cancellation));
        Assert.Equal(["WatchAsync", "GetWatchedAsync", "UnwatchAsync", "GetWatchedAsync"], site.Asked);
    }

    [Fact]
    public async Task RefusesToFollowAModByReferenceOnASiteWithoutAWatchlist()
    {
        Watchlists watchlists = new(new SourceRegistry([new CatalogOnlySource(TestData.Nexus)]), new InMemoryLibraryRepository());
        ModRef mod = TestData.File(TestData.Nexus, "1").Mod;

        await Assert.ThrowsAsync<ArgumentException>(() => watchlists.WatchAsync(mod, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => watchlists.UnwatchAsync(TestData.File(TestData.ModIo, "1").Mod, Cancellation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FollowingByReferencePassesOnWhatTheSiteRefuses(bool follow)
    {
        FakeWatchlistSource site = new(TestData.Nexus);
        AuthenticationFailedException failure = new("No account is linked.");
        site.Failures[follow ? "WatchAsync" : "UnwatchAsync"] = failure;
        Watchlists watchlists = new(new SourceRegistry([site]), new InMemoryLibraryRepository());

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => follow ? watchlists.WatchAsync(site.LinkedMod, Cancellation) : watchlists.UnwatchAsync(site.LinkedMod, Cancellation));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task RefusesToListASiteWithoutAWatchlist()
    {
        Watchlists watchlists = new(new SourceRegistry([new CatalogOnlySource(TestData.Nexus)]), new InMemoryLibraryRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => watchlists.GetWatchedAsync(TestData.Nexus, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => watchlists.GetWatchedAsync(TestData.ModIo, Cancellation));
    }

    [Fact]
    public void ListsTheSourcesWithAWatchlist()
    {
        Watchlists watchlists = new(new SourceRegistry(
            [
                new FakeWatchlistSource(TestData.Nexus),
                ScriptedLinkRecognizer.Never(new SourceId("direct")),
                new FakeUpdateFeed(new SourceId("feed-only")),
                new FakeWatchlistSource(TestData.ModIo),
            ]),
            new InMemoryLibraryRepository());

        Assert.Equal([TestData.Nexus, TestData.ModIo], watchlists.Sources);
    }
}
