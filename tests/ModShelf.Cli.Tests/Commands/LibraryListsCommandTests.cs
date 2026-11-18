using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library export</c> and <c>library import</c>: the library as a list, and a list brought into it. Mod 1 of the
/// catalog site has file 1; the handoff site's files wait for the user to start them on its page, as a free Nexus Mods
/// account's do.
/// </summary>
public sealed class LibraryListsCommandTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly ModRef Catalog = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");

    private readonly TempFolder _files = new();
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly RangeFileServer _server = new(new byte[1024], "file.zip");
    private readonly InProcessCli _cli;

    public LibraryListsCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeCatalogSite(), new FakeHandoffSource(_server.FileUrl)]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string ListFile => Path.Combine(_files.Path, "list.json");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _files.Dispose();
    }

    [Fact]
    public async Task ExportsTheLibraryToAFileAndImportsItBack()
    {
        await StoreAsync(pinned: true, note: "Keep at 1.1.");

        CliResult exported = await RunAsync("library", "export", "--to", ListFile);
        await RunAsync("library", "forget", FakeCatalogSite.ModLink("1").AbsoluteUri);
        CliResult imported = await RunAsync("library", "import", ListFile, "--watch-only", "--yes");

        Assert.Equal((Done, $"Wrote 1 mod and 1 file to {ListFile}."), (exported.ExitCode, exported.Output.Trim()));
        JsonObject list = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(ListFile, Cancellation))!;
        Assert.Equal(("modshelf-list", 1), ((string?)list["format"], (int?)list["version"]));
        Assert.Equal(
            [
                "The list has 1 mod: 1 new to the library and 0 in it already.",
                "Importing would watch the mods new to the library, and download nothing.",
                "Watched 1 new mod.",
            ],
            Lines(imported));
        Assert.Equal(Done, imported.ExitCode);
        LibraryMod mod = Assert.Single(await LibraryAsync());
        Assert.Equal((Catalog, true, true, "Keep at 1.1."), (mod.Mod, mod.IsWatched, mod.IsPinned, mod.Note));
    }

    [Fact]
    public async Task WritesTheListToTheOutputAndKeepsToAGame()
    {
        await StoreAsync();

        CliResult all = await RunAsync("library", "export");
        CliResult game = await RunAsync("library", "export", "--game", "fake-catalog/game");
        CliResult unknown = await RunAsync("library", "export", "--game", "fake-catalog/other");

        JsonArray mods = (JsonArray)JsonNode.Parse(all.Output)!["mods"]!;
        Assert.Equal("1", (string?)Assert.Single(mods)!["mod"]);
        Assert.Single((JsonArray)JsonNode.Parse(game.Output)!["mods"]!);
        Assert.Equal(Done, unknown.ExitCode);
        Assert.Empty((JsonArray)JsonNode.Parse(unknown.Output)!["mods"]!);
    }

    [Fact]
    public async Task SaysWhatImportingWouldDoUntilAskedToDoIt()
    {
        await File.WriteAllTextAsync(ListFile, List(("fake-handoff", "game", "mod", ["2", "3"]), ("elsewhere", "game", "9", [])), Cancellation);

        CliResult said = await RunAsync("library", "import", ListFile);

        Assert.Equal(
            [
                "The list has 2 mods: 1 new to the library, 0 in it already and 1 of sites ModShelf can't reach here (elsewhere).",
                "Importing would watch the new mods the list watches, and queue 2 files for the library.",
                "Run the command again with --yes to import the list.",
            ],
            Lines(said));
        Assert.Equal(Done, said.ExitCode);
        Assert.Empty(await LibraryAsync());
    }

    [Fact]
    public async Task QueuesTheFilesAndSaysWhichWaitForTheUser()
    {
        await File.WriteAllTextAsync(ListFile, List(("fake-handoff", "game", "mod", ["3"])), Cancellation);

        CliResult imported = await RunAsync("library", "import", ListFile, "--yes");

        Assert.Equal(Done, imported.ExitCode);
        string[] lines = Lines(imported);
        Assert.Contains("Watched 1 new mod. Queued 1 file for the library.", lines);
        Assert.Contains("1 file waits for you to start on its site, which hands it to ModShelf:", lines);
        Assert.Contains($"{FakeHandoffSource.FileNameOf("3")}: {FakeHandoffSource.Page("3").AbsoluteUri}", lines);
    }

    [Fact]
    public async Task SaysWhatItCouldntDoAndFails()
    {
        await File.WriteAllTextAsync(ListFile, List(("fake-handoff", "game", "mod", ["9"]), ("elsewhere", "game", "9", [])), Cancellation);

        CliResult imported = await RunAsync("library", "import", ListFile, "--yes");

        Assert.Equal(Failed, imported.ExitCode);
        Assert.Equal(
            [
                "Not done:",
                "mod (fake-handoff/game mod): its site no longer offers file 9.",
                "9 (elsewhere/game 9): ModShelf can't reach elsewhere here, so it's left out.",
                "--newest-for-missing downloads a mod's newest main file in place of one its site no longer offers.",
            ],
            Lines(imported)[^4..]);
    }

    [Theory]
    [InlineData("{}", "This isn't a ModShelf list: its \"format\" isn't \"modshelf-list\".")]
    [InlineData("""{"format": "modshelf-list", "version": 7}""", "This list was made by a newer ModShelf (list version 7); update ModShelf to import it.")]
    public async Task RefusesAFileThatIsntAListItCanRead(string text, string said)
    {
        await File.WriteAllTextAsync(ListFile, text, Cancellation);

        CliResult refused = await RunAsync("library", "import", ListFile);

        Assert.Equal((BadInput, said), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Fact]
    public async Task SaysWhenTheFileIsntThere()
    {
        CliResult refused = await RunAsync("library", "import", Path.Combine(_files.Path, "none.json"));

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("none.json", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportsACollectionByItsLinkSayingWhatItDoesntDownload()
    {
        string link = FakeCatalogSite.CollectionLink("essentials").AbsoluteUri;

        CliResult said = await RunAsync("library", "import", link);
        CliResult imported = await RunAsync("library", "import", link, "--watch-only", "--yes");

        Assert.Equal(
            [
                "The list \"Essentials\" has 1 mod: 1 new to the library and 0 in it already.",
                "1 file it lists is no longer offered by its site, so can't be downloaded.",
                "It needs these from elsewhere too, which ModShelf doesn't download:",
                "Script extender: https://extender.example/",
                "Importing would watch the new mods the list watches, and queue 1 file for the library.",
                "1 optional file is left out; --optional adds them.",
                "Run the command again with --yes to import the list.",
            ],
            Lines(said));
        Assert.Equal((Done, Done), (said.ExitCode, imported.ExitCode));
        Assert.Equal("Watched 1 new mod.", Lines(imported)[^1]);
        LibraryMod mod = Assert.Single(await LibraryAsync());
        Assert.Equal((Catalog, true, "Example Mod"), (mod.Mod, mod.IsWatched, mod.Name));
    }

    [Theory]
    [InlineData("gone", BadInput, "The site has no such collection.")]
    [InlineData("locked", Failed, "The site needs a linked account to read the collection, or refused the one linked. Link it with account link, then run the same command again.")]
    [InlineData("broken", Failed, "The site couldn't read the collection: Service unavailable.")]
    [InlineData("bad", BadInput, "That collection link names no collection.")]
    public async Task SaysWhyACollectionCantBeRead(string slug, int exitCode, string said)
    {
        CliResult refused = await RunAsync("library", "import", FakeCatalogSite.CollectionLink(slug).AbsoluteUri, "--yes");

        Assert.Equal((exitCode, said), (refused.ExitCode, refused.Errors.Trim()));
        Assert.Empty(await LibraryAsync());
    }

    [Fact]
    public async Task SaysWhenALinkIsntToACollection()
    {
        CliResult refused = await RunAsync("library", "import", "https://elsewhere.example/lists/mine");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.StartsWith("That link isn't to a collection ModShelf can read.", refused.Errors, StringComparison.Ordinal);
    }

    private static string List(params (string Site, string Game, string Mod, string[] Files)[] mods)
    {
        JsonArray listed = [];
        foreach ((string site, string game, string mod, string[] files) in mods)
        {
            JsonArray fileNodes = [];
            foreach (string file in files)
            {
                fileNodes.Add(new JsonObject { ["file"] = file });
            }

            listed.Add(new JsonObject { ["site"] = site, ["game"] = game, ["mod"] = mod, ["files"] = fileNodes });
        }

        return new JsonObject { ["format"] = "modshelf-list", ["version"] = 1, ["mods"] = listed }.ToJsonString();
    }

    private static string[] Lines(CliResult result)
        => result.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);

    private Task<IReadOnlyList<LibraryMod>> LibraryAsync()
        => _cli.InAppAsync(services => services.GetRequiredService<IModLibrary>().ListAsync(Cancellation), Cancellation);

    private Task<LibraryChange> StoreAsync(bool pinned = false, string? note = null) => _cli.InAppAsync(
        services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
            Catalog,
            _ => LibraryMod.Watch(Catalog, "Example Mod", "Someone", FakeCatalogSite.ModLink("1"), DateTimeOffset.UnixEpoch)
                .WithFile(new LibraryFile(
                    new ModFileRef(Catalog, "1"), "File 1", "1.1", ModFileCategory.Main, DateTimeOffset.UnixEpoch, "fake-catalog/game/1/file-1.zip", 1, DateTimeOffset.UnixEpoch))
                .WithPinned(pinned)
                .WithNote(note),
            Cancellation),
        Cancellation);
}
