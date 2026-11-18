using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library import-archives</c>: a folder of another mod manager's downloads, of the fake catalog site's game, whose
/// files are a megabyte each. One archive has Mod Organizer 2's note, one the site finds by its MD5, and one is unknown.
/// </summary>
public sealed class LibraryImportArchivesCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int BadInput = 2;

    private const int Megabyte = 1024 * 1024;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly FakeCatalogSite _catalog = new();
    private readonly InProcessCli _cli;

    public LibraryImportArchivesCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [_catalog]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Archives => Path.Combine(_cli.Folder, "MO2", "downloads");

    private string ModsFolder => Path.Combine(_cli.Downloads, "ModShelf", "fake-catalog", "game");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task SaysWhatImportingWouldDoAndChangesNothing()
    {
        await FillTheFolderAsync();

        CliResult previewed = await RunAsync("library", "import-archives", Archives, "--game", "fake-catalog/game");

        Assert.Equal(Done, previewed.ExitCode);
        Assert.Equal(
            [
                $"Identifying 3 archives of fake-catalog/game in {Archives} takes about 4 requests to Fake catalog site.",
                "  example.zip: Example Mod, Example Mod 1.0 (file 11 of mod 42), by its Mod Organizer note",
                "  looked-up.zip: Example Mod, Example Mod 2.0 (file 12 of mod 43), by its content",
                "  unknown.7z: unknown, left alone: Fake catalog site has no file of the game with its content: it may be from elsewhere, or changed since.",
                "Importing would copy 2 archives into the library, watching 2 mods new to it; 1 is unknown, left where it is.",
                "Run the command again with --yes to import them. The archives are only copied: nothing in the folder changes.",
            ],
            Lines(previewed));
        Assert.False(Directory.Exists(ModsFolder));
        Assert.Contains("The library is empty", (await RunAsync("library", "list")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopiesTheIdentifiedArchivesWatchingTheirModsAndLeavesTheFolderAsItWas()
    {
        (byte[] noted, byte[] lookedUp) = await FillTheFolderAsync();
        string[] before = [.. Directory.GetFiles(Archives).Order(StringComparer.Ordinal)];

        CliResult imported = await RunAsync("library", "import-archives", Archives, "--game", "fake-catalog/game", "--yes");
        CliResult again = await RunAsync("library", "import-archives", Archives, "--game", "fake-catalog/game");

        Assert.Equal(Done, imported.ExitCode);
        Assert.Equal("Copied 2 archives into the library, and watched 2 new mods.", Lines(imported)[^1]);
        Assert.Equal(noted, await File.ReadAllBytesAsync(Path.Combine(ModsFolder, "Example Mod (42)", "file-11.zip"), Cancellation));
        Assert.Equal(lookedUp, await File.ReadAllBytesAsync(Path.Combine(ModsFolder, "Example Mod (43)", "file-12.zip"), Cancellation));
        Assert.Equal(before, Directory.GetFiles(Archives).Order(StringComparer.Ordinal));
        Assert.Equal(noted, await File.ReadAllBytesAsync(Path.Combine(Archives, "example.zip"), Cancellation));
        string listed = (await RunAsync("library", "list", "--files")).Output;
        Assert.Contains("file-11.zip", listed, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(lookedUp)), listed, StringComparison.Ordinal);

        Assert.Equal(Done, again.ExitCode);
        Assert.Equal("  example.zip: in the library already, as Example Mod, Example Mod 1.0", Lines(again)[1]);
        Assert.Equal("Importing would copy nothing: 2 are in the library already and 1 is unknown, left where they are.", Lines(again)[^1]);
    }

    [Fact]
    public async Task SaysWhenTheFolderHasNoArchives()
    {
        Directory.CreateDirectory(Archives);
        await File.WriteAllTextAsync(Path.Combine(Archives, "readme.txt"), "Hello.", Cancellation);

        CliResult scanned = await RunAsync("library", "import-archives", Archives, "--game", "fake-catalog/game", "--yes");

        Assert.Equal(Done, scanned.ExitCode);
        Assert.Equal($"There are no archives (.7z, .zip or .rar) in {Archives}.", scanned.Output.Trim());
    }

    [Fact]
    public async Task RefusesAFolderThatIsntThereOrIsInTheLibraryOrAGameItCantIdentify()
    {
        string library = Path.Combine(_cli.Downloads, "ModShelf");
        Directory.CreateDirectory(library);

        CliResult missing = await RunAsync("library", "import-archives", Archives, "--game", "fake-catalog/game");
        CliResult inLibrary = await RunAsync("library", "import-archives", library, "--game", "fake-catalog/game");
        Directory.CreateDirectory(Archives);
        CliResult noGame = await RunAsync("library", "import-archives", Archives);
        CliResult badGame = await RunAsync("library", "import-archives", Archives, "--game", "elsewhere/game");

        Assert.Equal((BadInput, BadInput, BadInput, BadInput), (missing.ExitCode, inLibrary.ExitCode, noGame.ExitCode, badGame.ExitCode));
        Assert.Equal($"There's no folder at {Archives}.", missing.Errors.Trim());
        Assert.Contains("is in the library, whose files can't be imported into it.", inLibrary.Errors, StringComparison.Ordinal);
        Assert.Contains("--game", noGame.Errors, StringComparison.Ordinal);
        Assert.Contains("elsewhere can't list its mods' files, so archives of its games can't be identified.", badGame.Errors, StringComparison.Ordinal);
    }

    private static string[] Lines(CliResult result) => result.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    /// <summary>
    /// Puts the three archives, a note and a readme in the folder.
    /// </summary>
    /// <returns>The content of the archive with a note, and of the one found by its MD5.</returns>
    private async Task<(byte[] Noted, byte[] LookedUp)> FillTheFolderAsync()
    {
        Directory.CreateDirectory(Archives);
        byte[] noted = RandomNumberGenerator.GetBytes(Megabyte);
        byte[] lookedUp = RandomNumberGenerator.GetBytes(Megabyte);
        await File.WriteAllBytesAsync(Path.Combine(Archives, "example.zip"), noted, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Archives, "example.zip.meta"), "[General]\r\nmodID=42\r\nfileID=11\r\nrepository=Nexus\r\n", Cancellation);
        await File.WriteAllBytesAsync(Path.Combine(Archives, "looked-up.zip"), lookedUp, Cancellation);
        await File.WriteAllBytesAsync(Path.Combine(Archives, "unknown.7z"), RandomNumberGenerator.GetBytes(64), Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Archives, "readme.txt"), "Hello.", Cancellation);
        _catalog.ByMd5[Convert.ToHexStringLower(CryptographicOperations.HashData(HashAlgorithmName.MD5, lookedUp))] = ("43", FakeCatalogSite.NewestMainFile);
        return (noted, lookedUp);
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
