using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library contents</c>, over the library <see cref="ArchivedLibrary"/> puts together (ADR 0106).
/// </summary>
public sealed class LibraryContentsCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public LibraryContentsCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeCatalogSite()]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string ModLink => FakeCatalogSite.ModLink("1").AbsoluteUri;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListsTheModsArchiveAsATreeAndSaysHowToShowItsReadme()
    {
        string root = await ArchivedLibrary.StoreAsync(_cli, Cancellation);

        CliResult listed = await RunAsync("contents", ModLink);

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal(
            [
                "Lanterns.zip, a zip archive: 4 files in 4 folders, 82 B unpacked.",
                string.Empty,
                "Better Lanterns/",
                "  Data/",
                "    Textures/",
                "      lantern_d.dds  8 B",
                "    BetterLanterns.esp  4 B",
                "  Notes/",
                "    примечания.md  16 B",
                "  Readme.txt  54 B",
                string.Empty,
                $"Its readme is {ArchivedLibrary.Readme}. To show it: library contents \"{ModLink}\" --file \"Lanterns.zip\" --read \"{ArchivedLibrary.Readme}\"",
            ],
            Lines(listed));
        Assert.Equal(["Lanterns.esp", "Lanterns.zip"], Directory.GetFileSystemEntries(Path.Combine(root, "fake-catalog", "game", "1")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ShowsATextFileInTheArchive()
    {
        await ArchivedLibrary.StoreAsync(_cli, Cancellation);

        CliResult readme = await RunAsync("contents", ModLink, "--read", ArchivedLibrary.Readme);
        CliResult notes = await RunAsync("contents", ModLink, "--file", "12", "--read", "Better Lanterns\\Notes\\примечания.md");

        Assert.Equal((Done, Done), (readme.ExitCode, notes.ExitCode));
        Assert.Equal(ArchivedLibrary.ReadmeText, readme.Output.ReplaceLineEndings("\n")[..^1]);
        Assert.Equal("# Заметки", notes.Output.Trim());
        Assert.Empty(readme.Errors);
    }

    [Fact]
    public async Task AModWithMoreThanOneArchiveNeedsOneNamedAndADamagedOneIsSaid()
    {
        await ArchivedLibrary.StoreAsync(_cli, Cancellation);
        await ArchivedLibrary.AddDamagedAsync(_cli, Cancellation);

        CliResult unnamed = await RunAsync("contents", ModLink);
        CliResult wrong = await RunAsync("contents", ModLink, "--file", "Lanterns.rar");
        CliResult damaged = await RunAsync("contents", ModLink, "--file", "lanterns patch.7z");
        CliResult byId = await RunAsync("contents", ModLink, "--file", "12");

        Assert.Equal((BadInput, BadInput, Failed, Done), (unnamed.ExitCode, wrong.ExitCode, damaged.ExitCode, byId.ExitCode));
        Assert.StartsWith("Example Mod has 2 archives in the library. Name one with --file:", unnamed.Errors, StringComparison.Ordinal);
        Assert.Contains("Lanterns Patch.7z", unnamed.Errors, StringComparison.Ordinal);
        Assert.StartsWith("Example Mod has no archive in the library called Lanterns.rar.", wrong.Errors, StringComparison.Ordinal);
        Assert.Equal("Lanterns Patch.7z can't be read as a 7z archive: it's damaged, cut short, or isn't one.", damaged.Errors.Trim());
        Assert.StartsWith("Lanterns.zip, a zip archive", byId.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhatsWrongWithTheModTheFileOrTheEntry()
    {
        await ArchivedLibrary.StoreAsync(_cli, Cancellation);

        CliResult unknown = await RunAsync("contents", FakeCatalogSite.ModLink("5").AbsoluteUri);
        CliResult plugin = await RunAsync("contents", ModLink, "--file", "Lanterns.esp");
        CliResult missing = await RunAsync("contents", ModLink, "--read", "Better Lanterns/Other.txt");
        CliResult binary = await RunAsync("contents", ModLink, "--read", "Better Lanterns/Data/Textures/lantern_d.dds");
        CliResult notALink = await RunAsync("contents", "Lanterns.zip");

        Assert.Equal((Failed, BadInput, Failed, Failed, BadInput), (unknown.ExitCode, plugin.ExitCode, missing.ExitCode, binary.ExitCode, notALink.ExitCode));
        Assert.Equal($"The library has no mod at {FakeCatalogSite.ModLink("5")}.", unknown.Errors.Trim());
        Assert.StartsWith("Example Mod has no archive in the library called Lanterns.esp.", plugin.Errors, StringComparison.Ordinal);
        Assert.Equal("Lanterns.zip has no file Better Lanterns/Other.txt.", missing.Errors.Trim());
        Assert.Equal("Better Lanterns/Data/Textures/lantern_d.dds in Lanterns.zip isn't text.", binary.Errors.Trim());
    }

    private static string[] Lines(CliResult result) => result.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(["library", .. arguments], Cancellation);
}
