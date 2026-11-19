using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>library_cleanup</c>, which lists what could go from the library <see cref="ClutteredLibrary"/> puts together, and
/// deletes nothing (ADR 0104).
/// </summary>
public sealed class McpLibraryCleanupTests : IAsyncDisposable
{
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public McpLibraryCleanupTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeCatalogSite()]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListsWhatCouldGoByGroupAndDeletesNothing()
    {
        string root = await ClutteredLibrary.StoreAsync(_cli, Cancellation);
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        CleanupListing listing = await mcp.CallAsync<CleanupListing>("library_cleanup", null, Cancellation);

        Assert.Equal(
            "4 files, 27 bytes, can go; 1 are kept. This server deletes nothing: the user deletes them in the desktop app (Library, Free up space…) or with ms library cleanup --delete <group> --yes.",
            listing.Message);
        Assert.Equal(
            [
                new CleanupGroupTotal("archived", 1, 6, 1),
                new CleanupGroupTotal("duplicates", 1, 7, 0),
                new CleanupGroupTotal("unwatched", 1, 6, 0),
                new CleanupGroupTotal("unmatched", 1, 8, 0),
            ],
            listing.Groups);
        Assert.Equal((27L, 5, 0, false), (listing.CanGoBytes, listing.Total, listing.Offset, listing.HasMore));
        Assert.Equal(
            [
                new CleanupFileEntry("archived", "fake-catalog", "game", "1", "Example Mod", "30", null, InRoot(root, "fake-catalog/game/1/_archive/file-30.zip"), 6, null, true, null, null),
                new CleanupFileEntry("archived", "fake-catalog", "game", "1", "Example Mod", "40", null, InRoot(root, "fake-catalog/game/1/_archive/file-40.zip"), 5, null, false, "notOffered", null),
                new CleanupFileEntry(
                    "duplicates", "fake-catalog", "game", "5", "Other Mod", "20", null, InRoot(root, "fake-catalog/game/5/file-20.zip"), 7, InRoot(root, "fake-catalog/game/1/file-12.zip"), true, null, null),
                new CleanupFileEntry("unwatched", "fake-catalog", "game", "5", "Other Mod", "11", null, InRoot(root, "fake-catalog/game/5/file-11.zip"), 6, null, true, null, null),
                new CleanupFileEntry("unmatched", "fake-catalog", "game", "1", "Example Mod", null, null, InRoot(root, "fake-catalog/game/1/Readme.txt"), 8, null, true, null, null),
            ],
            listing.Files);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(root, "fake-catalog"), "*", SearchOption.AllDirectories).Length);
        Assert.True((await mcp.Client.ListToolsAsync(cancellationToken: Cancellation)).Single(tool => tool.Name == "library_cleanup").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task ListsAPageOfAGameASiteOrWhatCanGo()
    {
        await ClutteredLibrary.StoreAsync(_cli, Cancellation);
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        CleanupListing canGo = await mcp.CallAsync<CleanupListing>("library_cleanup", Arguments(("canGoOnly", true)), Cancellation);
        CleanupListing page = await mcp.CallAsync<CleanupListing>("library_cleanup", Arguments(("offset", 1), ("limit", 2)), Cancellation);
        CleanupListing site = await mcp.CallAsync<CleanupListing>("library_cleanup", Arguments(("source", "fake-catalog"), ("game", "GAME")), Cancellation);
        CleanupListing otherGame = await mcp.CallAsync<CleanupListing>("library_cleanup", Arguments(("game", "other")), Cancellation);
        string refused = await mcp.CallFailingAsync("library_cleanup", Arguments(("limit", 501)), Cancellation);

        Assert.Equal((4, 4), (canGo.Total, canGo.Files.Count));
        Assert.All(canGo.Files, file => Assert.True(file.CanGo));
        Assert.Equal((5, 1, true), (page.Total, page.Offset, page.HasMore));
        Assert.Equal(["40", "20"], page.Files.Select(file => file.FileId));
        Assert.Equal(5, site.Total);
        Assert.Equal((0, 0L), (otherGame.Total, otherGame.CanGoBytes));
        Assert.Equal("Nothing to free up: no archived old versions, duplicates, files of mods not watched or files that match nothing.", otherGame.Message);
        Assert.EndsWith("limit must be from 1 to 500.", refused, StringComparison.Ordinal);
    }

    private static string InRoot(string root, string relativePath) => LibraryPaths.Resolve(root, relativePath);

    private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        => arguments.ToDictionary(argument => argument.Name, argument => argument.Value);
}
