using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod games --installed</c>: the games installed on this computer, each with its games on the sites. Every run names
/// the fake site, so no other site is asked for its games.
/// </summary>
public sealed class ModCommandInstalledTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeCatalogSite _catalog = new();
    private readonly ScriptedInstalledGames _installed = new();
    private readonly InProcessCli _cli;
    private readonly InstalledGame _example;
    private readonly InstalledGame _abyss;
    private readonly InstalledGame _picked;
    private readonly InstalledGame _none;

    public ModCommandInstalledTests()
    {
        _catalog.MoreGames.AddRange(
        [
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "abyss"), "Abyss Game"),
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "abyss2"), "Abyss Game"),
            new GameInfo(new GameRef(FakeCatalogSite.SourceId, "zoo"), "Zoo"),
        ]);
        _example = _installed.Add("1", "Example Game");
        _abyss = _installed.Add("2", "Abyss Game");
        _picked = _installed.Add("3", "Zoo Tycoon Deluxe");
        _none = _installed.Add("4", "Zoo Story");
        _installed.Add("5", "Unmatched");
        _cli = new InProcessCli(_site, _browser, sources: [_catalog], configureServices: services =>
        {
            services.RemoveAll<IInstalledGames>();
            services.AddSingleton<IInstalledGames>(_installed);
        });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListsEachInstalledGameWithItsGamesOnTheSite()
    {
        await PickAsync();

        CliResult listed = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog"], Cancellation);

        string[] lines = Lines(listed);
        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal("Steam has 5 games installed, 3 with a site's game.", lines[0]);
        Assert.Equal(
            [
                "Example Game | steam 1 | fake-catalog | game | same name",
                "Abyss Game | steam 2 | fake-catalog | abyss | one of the same name",
                " | fake-catalog | abyss2 | one of the same name",
                "Zoo Tycoon Deluxe | steam 3 | fake-catalog | zoo | chosen",
                "Zoo Story | steam 4 | fake-catalog | - | none, chosen",
                "Unmatched | steam 5 | - | - | no site's game",
            ],
            Rows(lines));
        Assert.Contains("To search a game's mods: mod search --game <its key> --source <its site>", listed.Output, StringComparison.Ordinal);
        Assert.Contains("Choose an installed game's games in the desktop app's Settings › Games.", listed.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WordsKeepTheGamesWhoseNamesOrTheirGamesNamesHaveThem()
    {
        await PickAsync();

        CliResult byInstalledName = await _cli.RunAsync(["mod", "games", "abyss", "--installed", "--source", "fake-catalog"], Cancellation);
        CliResult byGameName = await _cli.RunAsync(["mod", "games", "zoo", "--installed", "--source", "fake-catalog"], Cancellation);
        CliResult none = await _cli.RunAsync(["mod", "games", "quake", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal("1 installed game matches.", Lines(byInstalledName)[0]);
        Assert.Equal("2 installed games match.", Lines(byGameName)[0]);
        Assert.Equal("No installed game is named so: \"quake\".", none.Output.Trim());
    }

    [Fact]
    public async Task WordsFindAGameByTheNameOfItsGameOnASite()
    {
        InstalledGame unmatched = _installed.Games[^1];
        await _cli.InAppAsync(
            async services =>
            {
                await services.GetRequiredService<IInstalledGameMatcher>().PickAsync(unmatched, FakeCatalogSite.SourceId, new GameRef(FakeCatalogSite.SourceId, "abyss2"), Cancellation);
                return true;
            },
            Cancellation);

        CliResult listed = await _cli.RunAsync(["mod", "games", "abyss", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal("2 installed games match.", Lines(listed)[0]);
        Assert.Contains("Unmatched | steam 5 | fake-catalog | abyss2 | chosen", Rows(Lines(listed)));
    }

    [Fact]
    public async Task ASourceKeepsEachGamesGamesThereAlone()
    {
        await _cli.InAppAsync(
            async services =>
            {
                await services.GetRequiredService<IInstalledGameMatcher>().PickAsync(_example, new SourceId("nexus"), null, Cancellation);
                return true;
            },
            Cancellation);

        CliResult listed = await _cli.RunAsync(["mod", "games", "example", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal(["Example Game | steam 1 | fake-catalog | game | same name"], Rows(Lines(listed)));
    }

    [Fact]
    public async Task ListsTheInstalledGamesAPageAtATime()
    {
        CliResult first = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog", "--page-size", "2"], Cancellation);
        CliResult past = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog", "--page-size", "2", "--page", "4"], Cancellation);

        Assert.Equal("Steam has 5 games installed, 2 with a site's game. These are 1 to 2.", Lines(first)[0]);
        Assert.Contains("More on the next page: add --page 2.", first.Output, StringComparison.Ordinal);
        Assert.Equal("No installed games on that page: Steam has 5 games installed, 2 with a site's game, 2 to a page.", past.Output.Trim());
    }

    [Fact]
    public async Task WithoutSteamSaysThereAreNoInstalledGames()
    {
        _installed.HasSteam = false;

        CliResult listed = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal((Done, "Steam isn't on this computer, so there are no installed games."), (listed.ExitCode, listed.Output.Trim()));
    }

    [Fact]
    public async Task WithSteamButNoGamesSaysSo()
    {
        _installed.Games.Clear();

        CliResult listed = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal("Steam has no games installed.", listed.Output.Trim());
    }

    [Fact]
    public async Task ASiteThatCantListItsGamesIsSaidAndItsGamesMatchOnlyAsChosen()
    {
        await PickAsync();
        _catalog.GamesFailure = new HttpRequestException("The site is down.");

        CliResult listed = await _cli.RunAsync(["mod", "games", "--installed", "--source", "fake-catalog"], Cancellation);

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal("Fake catalog site couldn't list its games, so they match only as chosen: The site is down.", listed.Errors.Trim());
        Assert.Equal("Steam has 5 games installed, 1 with a site's game.", Lines(listed)[0]);
    }

    [Fact]
    public async Task ASiteThatCantListGamesIsRefused()
    {
        CliResult refused = await _cli.RunAsync(["mod", "games", "--installed", "--source", "nowhere"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.StartsWith("'nowhere' can't list its games. Sites that can: ", refused.Errors.Trim(), StringComparison.Ordinal);
    }

    private static string[] Lines(CliResult result) => result.Output.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// The table's rows, their cells joined by " | ", without the headings: empty cells at the start of a row are one.
    /// </summary>
    private static string[] Rows(string[] lines)
        => [.. lines.Skip(2).TakeWhile(line => !line.StartsWith("To search", StringComparison.Ordinal) && line.Length > 0).Select(Cells)];

    private static string Cells(string line)
        => string.Join(" | ", System.Text.RegularExpressions.Regex.Split(line.TrimEnd(), "  +"));

    /// <summary>
    /// Picks Zoo for Zoo Tycoon Deluxe and none for Zoo Story, as the desktop app's Settings › Games does.
    /// </summary>
    private Task<bool> PickAsync()
        => _cli.InAppAsync(
            async services =>
            {
                IInstalledGameMatcher matcher = services.GetRequiredService<IInstalledGameMatcher>();
                await matcher.PickAsync(_picked, FakeCatalogSite.SourceId, new GameRef(FakeCatalogSite.SourceId, "zoo"), Cancellation);
                await matcher.PickAsync(_none, FakeCatalogSite.SourceId, null, Cancellation);
                return true;
            },
            Cancellation);
}
