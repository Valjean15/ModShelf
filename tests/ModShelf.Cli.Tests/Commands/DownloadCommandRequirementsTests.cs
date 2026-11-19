using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeRequiringSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>download &lt;link&gt; --with-requirements [--deep] [--yes]</c>: what the file's mod needs, listed, and with
/// <c>--yes</c>, the mods the library lacks downloaded into it too, each by its newest main file (ADR 0102). Each command
/// is a run of the app of its own.
/// </summary>
public sealed class DownloadCommandRequirementsTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int BadInput = 2;

    private const string Note = "The site's list can be wrong or out of date: check the mod's description too. Nothing is installed.";

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024);

    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public DownloadCommandRequirementsTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeRequiringSource(_server.FileUrl)]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task WithoutYesOnlyListsWhatTheModNeeds()
    {
        CliResult listed = await RunAsync("download", FileLink("main").AbsoluteUri, "--with-requirements");

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal(
            [
                "What mod main of game needs, as Fake requiring site lists it:",
                "  Lib Mod: downloads its newest main file, Lib Mod 1.0 (file 1 of mod lib)",
                $"  Script Extender: off the site, at {ExtenderPage}",
                "    Get the build for your game's version.",
                "  Bare Mod: can't be downloaded: Fake requiring site lists no main file of it to download.",
                Note,
                "Run the command again with --yes to download it with them, or without --with-requirements to download it alone.",
            ],
            Lines(listed));
        Assert.Empty(listed.Errors);
        Assert.Empty(_server.Ranges);
        Assert.Empty(Rows(await RunAsync("library", "list")));
    }

    [Fact]
    public async Task WithYesDownloadsTheFileAndTheModsItNeedsIntoTheLibrary()
    {
        CliResult downloaded = await RunAsync("download", FileLink("main").AbsoluteUri, "--with-requirements", "--yes");

        Assert.Equal(Done, downloaded.ExitCode);
        string[] lines = Lines(downloaded);
        Assert.Equal(Note, lines[5]);
        Assert.Contains($"Downloading {FileNameOf("main")} to {ModFolder("main")}", lines);
        Assert.Contains($"Downloading {FileNameOf("lib")}, which it needs, to {ModFolder("lib")}", lines);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder("main"), FileNameOf("main")), Cancellation));
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder("lib"), FileNameOf("lib")), Cancellation));
        Assert.Equal(["lib", "main"], Rows(await RunAsync("library", "list")).Select(row => row[1]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeepAlsoListsAndDownloadsWhatThoseModsNeed()
    {
        CliResult downloaded = await RunAsync("download", ModLink("main").AbsoluteUri, "--with-requirements", "--deep", "--yes");

        Assert.Equal(Done, downloaded.ExitCode);
        string[] lines = Lines(downloaded);
        Assert.Equal(
            [
                "  Lib Mod: downloads its newest main file, Lib Mod 1.0 (file 1 of mod lib)",
                "    Core Mod (needed by Lib Mod): downloads its newest main file, Core Mod 1.0 (file 1 of mod core)",
                $"  Script Extender: off the site, at {ExtenderPage}",
            ],
            lines[1..4]);
        Assert.Contains($"Downloading {FileNameOf("core")}, which it needs, to {ModFolder("core")}", lines);
        Assert.Equal(
            ["core", "lib", "main"],
            Rows(await RunAsync("library", "list")).Select(row => row[1]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LeavesOutTheModsTheLibraryHasAlready()
    {
        await RunAsync("download", FileLink("lib").AbsoluteUri);

        CliResult downloaded = await RunAsync("download", FileLink("main").AbsoluteUri, "--with-requirements", "--yes");

        Assert.Equal(Done, downloaded.ExitCode);
        Assert.Equal("  Lib Mod: in the library already", Lines(downloaded)[1]);
        Assert.DoesNotContain(Lines(downloaded), line => line.Contains("which it needs", StringComparison.Ordinal));
        Assert.Equal(["lib", "main"], Rows(await RunAsync("library", "list")).Select(row => row[1]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AModThatNeedsNothingDownloadsAlone()
    {
        CliResult downloaded = await RunAsync("download", FileLink("core").AbsoluteUri, "--with-requirements");

        Assert.Equal(Done, downloaded.ExitCode);
        Assert.Equal("Fake requiring site lists nothing mod core of game needs.", Lines(downloaded)[0]);
        Assert.Contains($"Downloading {FileNameOf("core")} to {ModFolder("core")}", Lines(downloaded));
        Assert.Equal(["core"], Rows(await RunAsync("library", "list")).Select(row => row[1]));
    }

    [Theory]
    [InlineData("--deep")]
    [InlineData("--yes")]
    public async Task RefusesDeepOrYesWithoutWithRequirements(string option)
    {
        CliResult refused = await RunAsync("download", FileLink("main").AbsoluteUri, option);

        Assert.Equal((BadInput, "--deep and --yes work with --with-requirements only."), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task RefusesWithRequirementsIntoAFolder()
    {
        using TempFolder folder = new();

        CliResult refused = await RunAsync("download", FileLink("main").AbsoluteUri, "--with-requirements", "--to", folder.Path);

        Assert.Equal(
            (BadInput, "--with-requirements downloads into the library only: leave out --to."),
            (refused.ExitCode, refused.Errors.Trim()));
        Assert.Empty(_server.Ranges);
    }

    private static string[] Lines(CliResult result) => result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// The rows of <c>library list</c>, without its heading, split on spaces.
    /// </summary>
    private static string[][] Rows(CliResult result)
        =>
        [
            .. result.Output.ReplaceLineEndings("\n").Split('\n')
                .Where(line => line.StartsWith("game ", StringComparison.Ordinal))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
        ];

    private string ModFolder(string mod) => Path.Combine(_cli.Downloads, "ModShelf", "fake-requiring", "game", $"{ModName(mod)} ({mod})");

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
