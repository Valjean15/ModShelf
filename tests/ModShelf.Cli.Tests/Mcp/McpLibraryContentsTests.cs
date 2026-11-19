using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>library_contents</c>, which shows what the archive of the library <see cref="ArchivedLibrary"/> puts together holds,
/// changing nothing (ADR 0106).
/// </summary>
public sealed class McpLibraryContentsTests : IAsyncDisposable
{
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public McpLibraryContentsTests()
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
    public async Task ListsTheArchiveAPageAtATimeWithItsReadme()
    {
        await ArchivedLibrary.StoreAsync(_cli, Cancellation);
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        ContentsListing first = await mcp.CallAsync<ContentsListing>("library_contents", Arguments(("link", ModLink), ("limit", 3)), Cancellation);
        ContentsListing rest = await mcp.CallAsync<ContentsListing>("library_contents", Arguments(("link", ModLink), ("offset", 3)), Cancellation);

        Assert.Equal(
            $"Lanterns.zip, a zip archive, holds 4 files in 4 folders, 82 bytes unpacked. Its readme is {ArchivedLibrary.Readme}.",
            first.Message);
        Assert.Equal(("zip", 4, 4, 82L, 8, 0, true), (first.Format, first.Files, first.Folders, first.UnpackedBytes, first.Total, first.Offset, first.HasMore));
        Assert.Equal(
            [
                new ContentsEntry("Better Lanterns", 0, IsFolder: true),
                new ContentsEntry("Better Lanterns/Data", 0, IsFolder: true),
                new ContentsEntry("Better Lanterns/Data/Textures", 0, IsFolder: true),
            ],
            first.Entries);
        Assert.Equal(new ContentsText(ArchivedLibrary.Readme, ArchivedLibrary.ReadmeText, 54, IsCut: false), first.Text);
        Assert.Equal((5, false), (rest.Entries.Count, rest.HasMore));
        Assert.True((await mcp.Client.ListToolsAsync(cancellationToken: Cancellation)).Single(tool => tool.Name == "library_contents").ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task GivesTheTextFileAskedForAndSaysWhatsWrong()
    {
        await ArchivedLibrary.StoreAsync(_cli, Cancellation);
        await ArchivedLibrary.AddDamagedAsync(_cli, Cancellation);
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        ContentsListing notes = await mcp.CallAsync<ContentsListing>(
            "library_contents", Arguments(("link", ModLink), ("file", "Lanterns.zip"), ("read", "Better Lanterns/Notes/примечания.md")), Cancellation);
        InvalidOperationException unnamed = await Assert.ThrowsAsync<InvalidOperationException>(() => mcp.CallAsync<ContentsListing>("library_contents", Arguments(("link", ModLink)), Cancellation));
        InvalidOperationException damaged = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mcp.CallAsync<ContentsListing>("library_contents", Arguments(("link", ModLink), ("file", "20")), Cancellation));

        Assert.Equal(new ContentsText("Better Lanterns/Notes/примечания.md", "# Заметки", 16, IsCut: false), notes.Text);
        Assert.Contains(
            "Example Mod has 2 archives in the library: name one with file. Its archives: Lanterns Patch.7z (ID 20), Lanterns.zip (ID 12).",
            unnamed.Message,
            StringComparison.Ordinal);
        Assert.Contains("Lanterns Patch.7z can't be read as a 7z archive", damaged.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        => arguments.ToDictionary(argument => argument.Name, argument => argument.Value);
}
