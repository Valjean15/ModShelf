using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
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
        Assert.Contains("44 mods match, most endorsed first. These are 1 to 20.", found.Output, StringComparison.Ordinal);
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
    [InlineData(Failed, "couldn't search: Field 'nameStemmed' is not defined.", "broken")]
    [InlineData(Failed, "account link", "locked")]
    public async Task SaysWhyItCantSearch(int exitCode, string message, params string[] arguments)
    {
        CliResult refused = await SearchAsync(arguments);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
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
