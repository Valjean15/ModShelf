using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod tracked</c>, <c>mod track</c> and <c>mod untrack</c> against a fake site's watchlist. Nexus Mods has one too,
/// so listing names the fake site.
/// </summary>
public sealed class ModCommandTrackingTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeCatalogSite _catalog = new();
    private readonly InProcessCli _cli;

    public ModCommandTrackingTests()
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
    public async Task ListsTheTrackedModsByGameThenNumber()
    {
        CliResult listed = await TrackedAsync();

        Assert.Equal(Done, listed.ExitCode);
        Assert.Contains("The account tracks 4 mods on fake-catalog.", listed.Output, StringComparison.Ordinal);
        Assert.Equal(["game 7", "game 120", "game 1000", "othergame 5"], Rows(listed));
        Assert.Contains("To stop tracking one: mod untrack", listed.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TracksTheModOfALink()
    {
        CliResult tracked = await _cli.RunAsync(["mod", "track", FileLink("42", "12").AbsoluteUri], Cancellation);

        Assert.Equal(Done, tracked.ExitCode);
        Assert.Equal("Tracking mod 42 of game on fake-catalog.", tracked.Output.Trim());
        Assert.Contains(Mod("42"), _catalog.Tracked);
        Assert.Contains("game 42", Rows(await TrackedAsync()));
    }

    [Fact]
    public async Task TrackingAModAgainChangesNothing()
    {
        CliResult tracked = await _cli.RunAsync(["mod", "track", ModLink("120").AbsoluteUri], Cancellation);

        Assert.Equal(Done, tracked.ExitCode);
        Assert.Equal(4, _catalog.Tracked.Count);
    }

    [Fact]
    public async Task UntracksTheModOfALink()
    {
        CliResult untracked = await _cli.RunAsync(["mod", "untrack", ModLink("120").AbsoluteUri], Cancellation);

        Assert.Equal(Done, untracked.ExitCode);
        Assert.Equal("No longer tracking mod 120 of game on fake-catalog.", untracked.Output.Trim());
        Assert.DoesNotContain("game 120", Rows(await TrackedAsync()));
    }

    [Fact]
    public async Task UntrackingAModNotTrackedChangesNothing()
    {
        CliResult untracked = await _cli.RunAsync(["mod", "untrack", ModLink("42").AbsoluteUri], Cancellation);

        Assert.Equal(Done, untracked.ExitCode);
        Assert.Equal(4, _catalog.Tracked.Count);
    }

    [Fact]
    public async Task SaysWhenNothingIsTracked()
    {
        foreach (ModRef mod in _catalog.Tracked)
        {
            await _catalog.UnwatchAsync(mod, Cancellation);
        }

        CliResult listed = await TrackedAsync();

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal("The account tracks no mods on fake-catalog.", listed.Output.Trim());
    }

    [Theory]
    [InlineData("track", "not a link", BadInput, "isn't a complete link")]
    [InlineData("track", "ftp://elsewhere.example/mod.zip", BadInput, "No source knows this link.")]
    [InlineData("untrack", "https://elsewhere.example/files/mod.zip", BadInput, "isn't to a mod on a site where ModShelf can track mods.")]
    [InlineData("track", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("track", "https://catalog.example/game/mods/401", Failed, "linked account to track mods")]
    [InlineData("track", "https://catalog.example/game/mods/503", Failed, "The site couldn't track the mod: Service unavailable.")]
    [InlineData("untrack", "https://catalog.example/game/mods/503", Failed, "The site couldn't stop tracking the mod: Service unavailable.")]
    public async Task SaysWhyItCantTrackOrUntrack(string command, string link, int exitCode, string message)
    {
        CliResult refused = await _cli.RunAsync(["mod", command, link], Cancellation);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.Equal(4, _catalog.Tracked.Count);
    }

    [Fact]
    public async Task SaysWhenTheSiteWantsAnAccount()
    {
        _catalog.WatchlistNeedsAccount = true;

        CliResult listed = await TrackedAsync();
        CliResult tracked = await _cli.RunAsync(["mod", "track", ModLink("42").AbsoluteUri], Cancellation);

        Assert.Equal((Failed, Failed), (listed.ExitCode, tracked.ExitCode));
        Assert.Contains("The site needs a linked account to track mods", listed.Errors, StringComparison.Ordinal);
        Assert.Contains("account link", tracked.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksWhichSiteWhenMoreThanOneCanTrackMods()
    {
        CliResult refused = await _cli.RunAsync(["mod", "tracked"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("More than one site can track mods; choose one with --source", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("nexus", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("fake-catalog", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesASiteThatCantTrackMods()
    {
        CliResult refused = await _cli.RunAsync(["mod", "tracked", "--source", "direct"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("'direct' can't track mods.", refused.Errors, StringComparison.Ordinal);
    }

    private static ModRef Mod(string id) => new(new GameRef(FakeCatalogSite.SourceId, "game"), id);

    /// <summary>
    /// The table's rows as "game id", without its heading.
    /// </summary>
    private static string[] Rows(CliResult result)
        =>
        [
            .. result.Output.ReplaceLineEndings("\n").Split('\n')
                .Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Where(line => line.StartsWith("game ", StringComparison.Ordinal) || line.StartsWith("othergame ", StringComparison.Ordinal)),
        ];

    private Task<CliResult> TrackedAsync() => _cli.RunAsync(["mod", "tracked", "--source", "fake-catalog"], Cancellation);
}
