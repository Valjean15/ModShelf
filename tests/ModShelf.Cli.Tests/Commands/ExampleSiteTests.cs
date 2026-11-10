using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// The example site, shaped unlike Nexus Mods, run through the app as the CLI runs it, beside Nexus Mods: its files go
/// into the library, its versions are found as updates, and its games are searched. Each command is a run of the app
/// of its own.
/// </summary>
public sealed class ExampleSiteTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int BadInput = 2;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(4 * 1024);
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly ExampleSite _example;
    private readonly InProcessCli _cli;

    public ExampleSiteTests()
    {
        _example = new ExampleSite(_server.FileUrl, Content);
        _cli = new InProcessCli(_site, _browser, sources: [_example]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string CropsFolder => Path.Combine(_cli.Downloads, "ModShelf", "example", "stardew-valley", "Better Crops (k7Qx2p)");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task DownloadsAModsNewestVersionIntoItsSitesAndGamesFolder()
    {
        CliResult downloaded = await RunAsync("download", ExampleSite.ModLink(Crops).AbsoluteUri);
        CliResult listed = await RunAsync("library", "list");

        Assert.Equal((Done, Done), (downloaded.ExitCode, listed.ExitCode));
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(CropsFolder, "better-crops-1.1.zip"), Cancellation));
        Assert.Contains("Added to the library.", downloaded.Output, StringComparison.Ordinal);
        Assert.Equal(
            ["stardew-valley", "k7Qx2p", "Better", "Crops", "yes", "1", ExampleSite.ModLink(Crops).AbsoluteUri],
            Words(Lines(listed)[2]));
    }

    [Fact]
    public async Task FindsANewVersionThoughTheSiteHasNoCategoriesOrChains()
    {
        await RunAsync("download", ExampleSite.ModLink(Crops).AbsoluteUri);
        CliResult before = await RunAsync("library", "check");
        _example.AddVersion(Crops, "v5e6f", "1.2", ExampleSite.Opened.AddDays(20));

        CliResult check = await RunAsync("library", "check");

        Assert.Equal(["Checked 1 mod with 1 request.", "No new updates."], Lines(before));
        string[] lines = Lines(check);
        Assert.Equal(["Checked 1 mod with 1 request.", "1 new update:"], lines[..2]);
        Assert.Equal(["Better", "Crops", "Better", "Crops", "1.2", "1.2"], Words(lines[3])[..6]);
        Assert.Equal(["better-crops-1.1.zip", ExampleSite.FileLink(Crops, "v5e6f").AbsoluteUri], Words(lines[3])[^2..]);
    }

    [Fact]
    public async Task ARescanFindsAnotherVersionPutInTheModsFolder()
    {
        await RunAsync("download", ExampleSite.ModLink(Crops).AbsoluteUri);
        await File.WriteAllBytesAsync(Path.Combine(CropsFolder, "better-crops-1.0.zip"), Content, Cancellation);

        CliResult rescan = await RunAsync("library", "rescan");

        Assert.Equal(Done, rescan.ExitCode);
        string[] lines = Lines(rescan);
        Assert.Equal("Added 1 file found on disk:", lines[1]);
        Assert.Contains(lines, line => line.Contains("better-crops-1.0.zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchesTheSitesGameByItsOwnKey()
    {
        CliResult found = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example");

        Assert.Equal(Done, found.ExitCode);
        string[] lines = Lines(found);
        Assert.Equal(["k7Qx2p", "Better", "Crops", "Ana"], Words(lines[2])[..4]);
        Assert.Equal(["Hq9wE2", "Golden", "Hoes", "Bo"], Words(lines[3])[..4]);
        Assert.EndsWith(ExampleSite.ModLink(Crops).AbsoluteUri, lines[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--sort", "Endorsements", "Example Mods can't sort by Endorsements. It can sort by Relevance, Downloads, Updated, Newest and Name.")]
    [InlineData("--author", "Ana", "Example Mods can't search by author. It can search by words and by when mods were updated.")]
    [InlineData("--page-size", "100", "Example Mods lists at most 50 mods a page.")]
    public async Task SaysWhatTheSiteCantSearchBy(string option, string value, string refusal)
    {
        CliResult refused = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example", option, value);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal(refusal, refused.Errors.Trim());
    }

    [Fact]
    public async Task PagesThroughItsGameAWholePageOfItsOwnSizeAtATime()
    {
        CliResult first = await RunAsync("mod", "search", "--game", "pixel-farm", "--source", "example");
        CliResult second = await RunAsync("mod", "search", "--game", "pixel-farm", "--source", "example", "--page-size", "50", "--page", "2");

        Assert.Contains("55 mods match, in the site's own order. These are 1 to 20.", first.Output, StringComparison.Ordinal);
        Assert.Contains("More on the next page: add --page 2.", first.Output, StringComparison.Ordinal);
        Assert.Contains("55 mods match, in the site's own order. These are 51 to 55.", second.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("More on the next page", second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsNoColumnForApprovalItDoesntCount()
    {
        CliResult found = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example");

        Assert.Equal(["ID", "Name", "Author", "Downloads", "Updated", "Link"], Words(Lines(found)[1]));
    }

    [Fact]
    public async Task IncludingAdultModsChangesNothingOnASiteWithoutThem()
    {
        CliResult without = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example");
        CliResult with = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example", "--adult");

        Assert.Equal((Done, without.Output), (with.ExitCode, with.Output));
    }

    [Theory]
    [InlineData("browse.sort", "size", "Example Mods can't sort by size. It can sort by relevance, downloads, updated, newest, name.")]
    [InlineData("browse.pageSize", "100", "Example Mods lists at most 50 mods a page.")]
    public async Task RefusesAGamesBrowseSettingItsSiteCantDo(string setting, string value, string refusal)
    {
        CliResult refused = await RunAsync("settings", "set", $"games.example/stardew-valley.{setting}", value);
        CliResult shown = await RunAsync("settings", "show");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal(refusal, refused.Errors.Trim());
        Assert.DoesNotContain("games.example/stardew-valley.browse", shown.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("browse.sort", "newest")]
    [InlineData("browse.direction", "ascending")]
    [InlineData("browse.window", "7d")]
    [InlineData("browse.pageSize", "50")]
    public async Task TakesAGamesBrowseSettingItsSiteCanDo(string setting, string value)
    {
        CliResult set = await RunAsync("settings", "set", $"games.example/stardew-valley.{setting}", value);

        Assert.Equal(Done, set.ExitCode);
    }

    [Fact]
    public async Task ListsItsGamesEachWithItsKey()
    {
        CliResult listed = await RunAsync("mod", "games", "--source", "example");

        Assert.Equal(Done, listed.ExitCode);
        string[] lines = Lines(listed);
        Assert.Equal("Example Mods lists 2 games. These are 1 to 2.", lines[0]);
        Assert.Equal(["Key", "Name"], Words(lines[1]));
        Assert.Equal(["pixel-farm", "Pixel", "Farm"], Words(lines[2]));
        Assert.Equal(["stardew-valley", "Stardew", "Valley"], Words(lines[3]));
        Assert.Equal("To search a game's mods: mod search --game <its key>", lines[^1]);
    }

    [Theory]
    [InlineData("valley")]
    [InlineData("VALLEY stardew")]
    [InlineData("stardew-valley")]
    public async Task FindsAGameByTheStartsOfItsNamesWords(string words)
    {
        CliResult found = await RunAsync(["mod", "games", .. words.Split(' '), "--source", "example"]);

        string[] lines = Lines(found);
        Assert.Equal("1 game on Example Mods matches.", lines[0]);
        Assert.Equal(["stardew-valley", "Stardew", "Valley"], Words(lines[2]));
    }

    [Fact]
    public async Task SaysWhenNoGameIsNamedSo()
    {
        CliResult found = await RunAsync("mod", "games", "dew", "--source", "example");

        Assert.Equal((Done, "No game on Example Mods is named so: \"dew\"."), (found.ExitCode, found.Output.Trim()));
    }

    [Fact]
    public async Task PagesThroughItsGames()
    {
        CliResult first = await RunAsync("mod", "games", "--source", "example", "--page-size", "1");
        CliResult second = await RunAsync("mod", "games", "--source", "example", "--page-size", "1", "--page", "2");
        CliResult beyond = await RunAsync("mod", "games", "--source", "example", "--page-size", "1", "--page", "3");

        Assert.Contains("More on the next page: add --page 2.", first.Output, StringComparison.Ordinal);
        Assert.StartsWith("Example Mods lists 2 games. These are 2 to 2.", second.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("More on the next page", second.Output, StringComparison.Ordinal);
        Assert.Equal("No games on that page: Example Mods lists 2 games, 1 to a page.", beyond.Output.Trim());
    }

    [Theory]
    [InlineData("--page-size", "0", "--page-size must be from 1 to 100.")]
    [InlineData("--page-size", "101", "--page-size must be from 1 to 100.")]
    [InlineData("--page", "0", "--page must be 1 or more, and not that many pages in.")]
    public async Task RefusesAPageItCantGive(string option, string value, string refusal)
    {
        CliResult refused = await RunAsync("mod", "games", "--source", "example", option, value);

        Assert.Equal((BadInput, refusal), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Fact]
    public async Task AsksWhichSitesGamesToListNowTwoHaveGames()
    {
        CliResult refused = await RunAsync("mod", "games", "valley");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.StartsWith("More than one site can list its games; choose one with --source: ", refused.Errors.Trim(), StringComparison.Ordinal);
        Assert.Contains("example", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksWhichSiteToSearchNowTwoCan()
    {
        CliResult unclear = await RunAsync("mod", "search", "golden", "--game", "stardew-valley");

        Assert.Equal(BadInput, unclear.ExitCode);
        Assert.Equal("More than one site can search; choose one with --source: nexus, example.", unclear.Errors.Trim());
    }

    [Fact]
    public async Task AGamesNumberAloneIsntNexusModsToSearch()
    {
        CliResult unclear = await RunAsync("mod", "search", "golden", "--game", "16951");

        Assert.Equal(BadInput, unclear.ExitCode);
        Assert.Equal("More than one site can search; choose one with --source: gamebanana, example.", unclear.Errors.Trim());
    }

    [Fact]
    public async Task ShowsEachVersionAsAMainFile()
    {
        CliResult shown = await RunAsync("mod", "show", ExampleSite.ModLink(Crops).AbsoluteUri);

        Assert.Equal(Done, shown.ExitCode);
        string[] lines = Lines(shown);
        int main = Array.IndexOf(lines, "Main files");
        Assert.Equal(["*", "v3c4d", "Better", "Crops", "1.1"], Words(lines[main + 2])[..5]);
        Assert.Equal(["v1a2b", "Better", "Crops", "1.0"], Words(lines[main + 3])[..4]);
    }

    [Fact]
    public async Task AGameNamedByItsKeyAloneIsTheOnlySitesThatTheLibraryHas()
    {
        await RunAsync("library", "watch", ExampleSite.ModLink(Crops).AbsoluteUri);

        CliResult set = await RunAsync("settings", "set", "games.Stardew-Valley.notify", "false");
        CliResult shown = await RunAsync("settings", "show");

        Assert.Equal(Done, set.ExitCode);
        Assert.StartsWith("games.example/Stardew-Valley.notify is now false:", set.Output.Trim(), StringComparison.Ordinal);
        Assert.Contains(Lines(shown), line => Words(line) is ["games.example/Stardew-Valley.notify", "false"]);
    }

    [Fact]
    public async Task AGameNamedByItsKeyAloneIsTheOnlySitesThatHasSettings()
    {
        await RunAsync("settings", "set", "games.example/stardew-valley.hidden", "true");

        CliResult reset = await RunAsync("settings", "reset", "games.stardew-valley.hidden");

        Assert.Equal("games.example/stardew-valley.hidden is gone, so the desktop app's lists show example/stardew-valley.", reset.Output.Trim());
    }

    [Fact]
    public async Task AGameNamedByAKeyAloneThatTwoSitesMayHaveNeedsItsSite()
    {
        CliResult refused = await RunAsync("settings", "set", "games.stardewvalley.notify", "false");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal(
            "More than one site may have a game called stardewvalley: name its site too, as nexus/stardewvalley or example/stardewvalley.",
            refused.Errors.Trim());
    }

    [Fact]
    public async Task TheSameKeyOnTwoSitesKeepsSettingsOfItsOwnOnEach()
    {
        await RunAsync("settings", "set", "games.nexus/stardew-valley.hidden", "true");
        await RunAsync("settings", "set", "games.example/stardew-valley.notify", "false");

        string[] shown = Lines(await RunAsync("settings", "show"));

        Assert.Contains(shown, line => Words(line) is ["games.nexus/stardew-valley.hidden", "true"]);
        Assert.Contains(shown, line => Words(line) is ["games.example/stardew-valley.notify", "false"]);
        Assert.DoesNotContain(shown, line => line.StartsWith("games.example/stardew-valley.hidden", StringComparison.Ordinal));
    }

    private static string[] Lines(CliResult result) => result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    private static string[] Words(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
