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
    [InlineData("--sort", "Endorsements", "Example Mods can't sort by Endorsements.")]
    [InlineData("--author", "Ana", "Example Mods can't search by author or category.")]
    public async Task SaysWhatTheSiteCantSearchBy(string option, string value, string refusal)
    {
        CliResult refused = await RunAsync("mod", "search", "--game", "stardew-valley", "--source", "example", option, value);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal(refusal, refused.Errors.Trim());
    }

    [Fact]
    public async Task AsksWhichSiteToSearchNowTwoCan()
    {
        CliResult unclear = await RunAsync("mod", "search", "golden", "--game", "stardew-valley");

        Assert.Equal(BadInput, unclear.ExitCode);
        Assert.Equal("More than one site can search; choose one with --source: nexus, example.", unclear.Errors.Trim());
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

    private static string[] Lines(CliResult result) => result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    private static string[] Words(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
