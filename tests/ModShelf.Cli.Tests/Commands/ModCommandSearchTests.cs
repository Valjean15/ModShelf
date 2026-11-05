using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod search</c> against a fake site's 45 mods. Nexus Mods can search too, so every search names the fake site.
/// </summary>
public sealed class ModCommandSearchTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeCatalogSite _catalog = new();
    private readonly InProcessCli _cli;

    public ModCommandSearchTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [_catalog]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListsTheFirstPageOfTheGamesModsWithTheirLinks()
    {
        CliResult found = await SearchAsync();

        Assert.Equal(Done, found.ExitCode);
        Assert.Contains("44 mods match, in the site's own order. These are 1 to 20.", found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("101").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("121").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ModLink("122").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains("Example Mod 4 (hidden)", found.Output, StringComparison.Ordinal);
        Assert.Contains("More on the next page: add --page 2.", found.Output, StringComparison.Ordinal);
        Assert.Equal(new SearchQuery(sort: SearchSort.Relevance, offset: 0, count: 20), _catalog.LastQuery);
    }

    [Fact]
    public async Task ALaterPageStartsWhereTheLastEnded()
    {
        CliResult found = await SearchAsync("--page", "3");

        Assert.Contains("These are 41 to 44.", found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("145").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("More on the next page", found.Output, StringComparison.Ordinal);
        Assert.Equal(40, _catalog.LastQuery!.Offset);
    }

    [Fact]
    public async Task WordsAuthorAndSortNarrowAndOrderTheSearch()
    {
        CliResult found = await SearchAsync("mod", "12", "--author", "Someone", "--sort", "downloads");

        Assert.Contains("1 mod matches, most downloaded first.", found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("These are", found.Output, StringComparison.Ordinal);
        Assert.Contains("Example Mod 12", found.Output, StringComparison.Ordinal);
        Assert.Equal(new SearchQuery("mod 12", SearchSort.Downloads, author: "Someone"), _catalog.LastQuery);
    }

    [Fact]
    public async Task APageHoldsAsManyModsAsAsked()
    {
        CliResult found = await SearchAsync("--page-size", "5", "--page", "2");

        Assert.Contains("44 mods match, in the site's own order. These are 6 to 10.", found.Output, StringComparison.Ordinal);
        // Mod 103 has adult content, so the sixth to tenth mods are 107 to 111.
        Assert.DoesNotContain(ModLink("106").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("107").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("111").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ModLink("112").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains("More on the next page: add --page 3.", found.Output, StringComparison.Ordinal);
        Assert.Equal((5, 5), (_catalog.LastQuery!.Offset, _catalog.LastQuery.Count));
    }

    [Fact]
    public async Task APageCanHoldAHundredMods()
    {
        CliResult found = await SearchAsync("--page-size", "100");

        Assert.Contains("These are 1 to 44.", found.Output, StringComparison.Ordinal);
        Assert.Equal(100, _catalog.LastQuery!.Count);
    }

    [Theory]
    [InlineData("--sort", "newest", ", newest first.", SearchDirection.Descending)]
    [InlineData("--sort", "newest", ", oldest first.", SearchDirection.Ascending)]
    [InlineData("--sort", "downloads", ", least downloaded first.", SearchDirection.Ascending)]
    [InlineData("--sort", "updated", ", longest since updated first.", SearchDirection.Ascending)]
    [InlineData("--sort", "relevance", ", in the site's own order turned around.", SearchDirection.Ascending)]
    [InlineData("--sort", "endorsements", ", most endorsements first.", SearchDirection.Descending)]
    [InlineData("--sort", "endorsements", ", fewest endorsements first.", SearchDirection.Ascending)]
    [InlineData("--sort", "name", ", by name from Z to A.", SearchDirection.Descending)]
    [InlineData("--sort", "name", ", by name from A to Z.", SearchDirection.Ascending)]
    [InlineData("--sort", "size", ", largest first.", SearchDirection.Descending)]
    [InlineData("--sort", "size", ", smallest first.", SearchDirection.Ascending)]
    [InlineData("--sort", "uniquedownloads", ", most downloaded by different users first.", SearchDirection.Descending)]
    [InlineData("--sort", "uniquedownloads", ", least downloaded by different users first.", SearchDirection.Ascending)]
    public async Task AscendingTurnsTheOrderAround(string option, string sort, string described, SearchDirection direction)
    {
        CliResult found = await SearchAsync(direction == SearchDirection.Ascending ? [option, sort, "--ascending"] : [option, sort]);

        Assert.Contains(described, found.Output, StringComparison.Ordinal);
        Assert.Equal(direction, _catalog.LastQuery!.Direction);
    }

    [Fact]
    public async Task AdultContentOnlyWhenAsked()
    {
        CliResult found = await SearchAsync("3", "--adult");

        Assert.Contains("Example Mod 3 (adult)", found.Output, StringComparison.Ordinal);
        Assert.True(_catalog.LastQuery!.IncludeAdultContent);
    }

    [Fact]
    public async Task SaysWhenNothingMatches()
    {
        Assert.Contains("No mods match.", (await SearchAsync("nothing", "like", "it")).Output, StringComparison.Ordinal);
        Assert.Contains("No mods on that page: 44 match", (await SearchAsync("--page", "9")).Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BadInput, "has no game 'elsewhere'", "--game", "elsewhere")]
    [InlineData(BadInput, "--page must be 1 or more", "--page", "0")]
    [InlineData(BadInput, "--page-size must be from 1 to 100", "--page-size", "0")]
    [InlineData(BadInput, "--page-size must be from 1 to 100", "--page-size", "101")]
    [InlineData(BadInput, "--sort must be one of Relevance, Endorsements, Downloads, Updated, Newest, Name, Size, UniqueDownloads.", "--sort", "9")]
    [InlineData(Failed, "couldn't search: Field 'nameStemmed' is not defined.", "broken")]
    [InlineData(Failed, "account link", "locked")]
    public async Task SaysWhyItCantSearch(int exitCode, string message, params string[] arguments)
    {
        CliResult refused = await SearchAsync(arguments);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesInTheSitesOwnOrderAndPageSizeUnlessAsked()
    {
        _catalog.Capabilities = new([.. SearchOrder.BothWays(SearchSort.Newest), .. SearchOrder.BothWays(SearchSort.Name)], SearchFilters.Text, 10);

        CliResult found = await SearchAsync();
        SearchQuery own = _catalog.LastQuery!;
        await SearchAsync("--ascending");
        SearchQuery turned = _catalog.LastQuery!;
        await SearchAsync("--sort", "name", "--page-size", "5");
        SearchQuery asked = _catalog.LastQuery!;

        Assert.Contains("44 mods match, newest first. These are 1 to 10.", found.Output, StringComparison.Ordinal);
        Assert.Equal((SearchSort.Newest, SearchDirection.Descending, 10), (own.Sort, own.Direction, own.Count));
        Assert.Equal((SearchSort.Newest, SearchDirection.Ascending, 10), (turned.Sort, turned.Direction, turned.Count));
        Assert.Equal((SearchSort.Name, SearchDirection.Descending, 5), (asked.Sort, asked.Direction, asked.Count));
    }

    [Fact]
    public async Task TheSitesOwnOrderGoesItsOwnWayAndAscendingTurnsItAround()
    {
        _catalog.Capabilities = new([new(SearchSort.Name, SearchDirection.Ascending), new(SearchSort.Name, SearchDirection.Descending)], SearchFilters.Text, 100);

        CliResult found = await SearchAsync();
        SearchQuery own = _catalog.LastQuery!;
        await SearchAsync("--ascending");
        SearchQuery turned = _catalog.LastQuery!;

        Assert.Contains("44 mods match, by name from A to Z.", found.Output, StringComparison.Ordinal);
        Assert.Equal((SearchSort.Name, SearchDirection.Ascending), (own.Sort, own.Direction));
        Assert.Equal((SearchSort.Name, SearchDirection.Descending), (turned.Sort, turned.Direction));
    }

    [Fact]
    public async Task RefusesAnOrderTheSiteHasOnlyTheOtherWayRound()
    {
        _catalog.Capabilities = new([new(SearchSort.Relevance, SearchDirection.Descending), new(SearchSort.Name, SearchDirection.Ascending)], SearchFilters.Text, 100);

        CliResult refused = await SearchAsync("--sort", "name");
        CliResult found = await SearchAsync("--sort", "name", "--ascending");

        Assert.Equal((BadInput, "Fake catalog site can sort by Name only with the least first."), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Equal(Done, found.ExitCode);
        Assert.Contains(", by name from A to Z.", found.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAGamesBrowseSettingsTheSiteCantDo()
    {
        _catalog.Capabilities = new([new(SearchSort.Relevance, SearchDirection.Descending), new(SearchSort.Name, SearchDirection.Ascending)], SearchFilters.Text, 100);

        CliResult sorted = await _cli.RunAsync(["settings", "set", "games.fake-catalog/game.browse.sort", "name"], Cancellation);
        CliResult wrongWay = await _cli.RunAsync(["settings", "set", "games.fake-catalog/game.browse.direction", "descending"], Cancellation);
        CliResult rightWay = await _cli.RunAsync(["settings", "set", "games.fake-catalog/game.browse.direction", "ascending"], Cancellation);
        CliResult window = await _cli.RunAsync(["settings", "set", "games.fake-catalog/game.browse.window", "7d"], Cancellation);

        Assert.Equal((Done, Done), (sorted.ExitCode, rightWay.ExitCode));
        Assert.Equal((BadInput, "Fake catalog site can sort by name only ascending."), (wrongWay.ExitCode, wrongWay.Errors.Trim()));
        Assert.Equal((BadInput, "Fake catalog site can't search by when mods were updated."), (window.ExitCode, window.Errors.Trim()));
    }

    [Fact]
    public async Task ACategoryAndHowLongSinceUpdatedNarrowTheSearch()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        CliResult found = await SearchAsync("--category", " armour ", "--updated-within", "7");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(Done, found.ExitCode);
        SearchQuery asked = _catalog.LastQuery!;
        Assert.Equal("Armour", asked.Category);
        DateTimeOffset since = Assert.NotNull(asked.UpdatedSince);
        Assert.Equal((TimeSpan.Zero, 0, 0, 0), (since.Offset, since.Minute, since.Second, since.Millisecond));
        Assert.InRange(since, before.AddDays(-7).AddHours(-1), after.AddDays(-7));
        Assert.Equal(1, _catalog.GameReads);
    }

    [Fact]
    public async Task WithoutACategoryTheGameIsntRead()
    {
        await SearchAsync("--updated-within", "1");

        Assert.Null(_catalog.LastQuery!.Category);
        Assert.Equal(0, _catalog.GameReads);
    }

    [Theory]
    [InlineData("Weapons", "Example Game has no category 'Weapons' on Fake catalog site. Its categories: Armour, Audio, Gameplay.")]
    [InlineData(" weap ", "Example Game has no category 'weap' on Fake catalog site. Its categories: Armour, Audio, Gameplay.")]
    public async Task RefusesACategoryTheGameDoesntHaveNamingThoseItHas(string category, string problem)
    {
        CliResult refused = await SearchAsync("--category", category);

        Assert.Equal((BadInput, problem), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Null(_catalog.LastQuery);
    }

    [Fact]
    public async Task SaysWhenTheGameHasNoCategories()
    {
        _catalog.Categories = [];

        CliResult refused = await SearchAsync("--category", "Armour");

        Assert.Equal((BadInput, "Example Game has no categories on Fake catalog site."), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Theory]
    [InlineData("Fake catalog site can't search by category. It can search by words.", "--category", "Armour")]
    [InlineData("Fake catalog site can't search by when mods were updated. It can search by words.", "--updated-within", "7")]
    public async Task ASiteThatCantFilterSoRefusesIt(string problem, params string[] arguments)
    {
        _catalog.Capabilities = new([new(SearchSort.Relevance, SearchDirection.Descending)], SearchFilters.Text, 50);

        CliResult refused = await SearchAsync(arguments);

        Assert.Equal((BadInput, problem), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Equal(0, _catalog.GameReads);
        Assert.Null(_catalog.LastQuery);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("36501")]
    public async Task RefusesDaysThatCantBe(string days)
    {
        CliResult refused = await SearchAsync("--updated-within", days);

        Assert.Equal((BadInput, "--updated-within must be from 1 to 36500 days."), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Null(_catalog.LastQuery);
    }

    [Fact]
    public async Task SaysWhenTheGamesCategoriesCantBeRead()
    {
        _catalog.GameFailure = new HttpRequestException("Service unavailable.");

        CliResult refused = await SearchAsync("--category", "Armour");

        Assert.Equal((Failed, "The site couldn't search: Service unavailable."), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Null(_catalog.LastQuery);
    }

    [Fact]
    public async Task NamesApprovalInTheSitesWords()
    {
        _catalog.Words = new() { Approval = "like", Approvals = "likes" };

        CliResult most = await SearchAsync("--sort", "endorsements");
        CliResult fewest = await SearchAsync("--sort", "endorsements", "--ascending");

        Assert.Contains("44 mods match, most likes first.", most.Output, StringComparison.Ordinal);
        Assert.Contains("44 mods match, fewest likes first.", fewest.Output, StringComparison.Ordinal);
        Assert.Equal(["ID", "Name", "Author", "Likes", "Downloads", "Updated", "Link"], most.Output.ReplaceLineEndings("\n").Split('\n')[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task ListsTheGamesThatStartWithTheWordsFirst()
    {
        _catalog.MoreGames.AddRange(
        [
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "abyss"), "Abyss Game"),
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "gamebox"), "Game Box"),
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "zoo"), "Zoo"),
        ]);

        CliResult found = await _cli.RunAsync(["mod", "games", "game", "--source", "fake-catalog"], Cancellation);

        string[] lines = found.Output.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("3 games on Fake catalog site match. These are 1 to 3.", lines[0]);
        Assert.Equal(["gamebox", "abyss", "game"], lines[2..5].Select(line => line.Split(' ')[0]));
    }

    [Fact]
    public async Task AsksWhichSiteWhenMoreThanOneCanSearch()
    {
        CliResult refused = await _cli.RunAsync(["mod", "search", "--game", "game"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("choose one with --source", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("nexus", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("fake-catalog", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesASiteThatCantSearch()
    {
        CliResult refused = await _cli.RunAsync(["mod", "search", "--game", "game", "--source", "direct"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("'direct' can't search", refused.Errors, StringComparison.Ordinal);
    }

    private Task<CliResult> SearchAsync(params string[] arguments)
    {
        string[] defaults = arguments.Contains("--game") ? ["--source", "fake-catalog"] : ["--game", "game", "--source", "fake-catalog"];
        return _cli.RunAsync(["mod", "search", .. arguments, .. defaults], Cancellation);
    }
}
