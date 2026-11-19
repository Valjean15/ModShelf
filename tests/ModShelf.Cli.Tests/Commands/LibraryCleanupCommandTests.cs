using System.Text.RegularExpressions;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library cleanup</c> against the fake catalog site, with the library <see cref="ClutteredLibrary"/> puts together.
/// </summary>
public sealed partial class LibraryCleanupCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;
    private string _root = string.Empty;

    public LibraryCleanupCommandTests()
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
        await StoreAsync();

        CliResult listed = await RunAsync("cleanup");

        Assert.Equal(Done, listed.ExitCode);
        string[] lines = Lines(listed);
        Assert.Equal(
            [
                ["Group", "Game", "Mod", "Path", "Size", "State"],
                ["archived", "game", "Example Mod", "fake-catalog/game/1/_archive/file-30.zip", "6 B", "can go"],
                ["archived", "game", "Example Mod", "fake-catalog/game/1/_archive/file-40.zip", "5 B", "kept: Fake catalog site no longer offers it"],
                ["duplicates", "game", "Other Mod", "fake-catalog/game/5/file-20.zip", "7 B", "can go: a copy of fake-catalog/game/1/file-12.zip"],
                ["unwatched", "game", "Other Mod", "fake-catalog/game/5/file-11.zip", "6 B", "can go"],
                ["unmatched", "game", "Example Mod", "fake-catalog/game/1/Readme.txt", "8 B", "can go"],
            ],
            lines[..6].Select(line => Columns().Split(line)));
        Assert.Equal(
            [
                "Archived old versions: 1 file can go, 6 B; 1 file kept.",
                "Duplicates: 1 file can go, 7 B.",
                "Files of mods not watched: 1 file can go, 6 B.",
                "Files that match nothing: 1 file can go, 8 B.",
                "In all, 4 files, 27 B, can go.",
                "Run library cleanup --delete <group> --yes to delete a group's files that can go: archived, duplicates, unwatched or unmatched.",
            ],
            lines[6..]);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(_root, "fake-catalog"), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task SaysWhatDeletingAGroupWouldDoAndDeletesItWithYes()
    {
        await StoreAsync();

        CliResult previewed = await RunAsync("cleanup", "--delete", "archived");
        CliResult archived = await RunAsync("cleanup", "--delete", "archived", "--yes");
        CliResult unmatched = await RunAsync("cleanup", "--delete", "unmatched", "--yes");
        CliResult again = await RunAsync("cleanup");

        Assert.Equal((Done, Done, Done), (previewed.ExitCode, archived.ExitCode, unmatched.ExitCode));
        Assert.Equal("Run it again with --yes to delete the 1 file of archived that can go, 6 B.", Lines(previewed)[^1]);
        Assert.Equal("Deleted 1 file, freeing 6 B.", Lines(archived)[^1]);
        Assert.Equal("Deleted 1 file, freeing 8 B.", Lines(unmatched)[^1]);
        Assert.False(File.Exists(InRoot("fake-catalog/game/1/_archive/file-30.zip")));
        Assert.False(File.Exists(InRoot("fake-catalog/game/1/Readme.txt")));
        Assert.True(File.Exists(InRoot("fake-catalog/game/1/_archive/file-40.zip")));
        Assert.Equal("In all, 2 files, 13 B, can go.", Lines(again)[^2]);
        string files = (await _cli.RunAsync(["library", "list", "--files"], Cancellation)).Output;
        Assert.DoesNotContain("file-30.zip", files, StringComparison.Ordinal);
        Assert.Contains("file-40.zip", files, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenNothingCanGo()
    {
        await StoreAsync();

        CliResult otherGame = await RunAsync("cleanup", "--game", "fake-catalog/other");
        await RunAsync("cleanup", "--delete", "duplicates", "--yes");
        CliResult noDuplicates = await RunAsync("cleanup", "--delete", "duplicates", "--yes");

        Assert.Equal((Done, Done), (otherGame.ExitCode, noDuplicates.ExitCode));
        Assert.Equal(
            "Nothing to free up: fake-catalog/other's files hold no archived old versions, duplicates, files of mods not watched or files that match nothing.",
            otherGame.Output.Trim());
        Assert.Equal("No duplicates can go.", Lines(noDuplicates)[^1]);
        Assert.True(File.Exists(InRoot("fake-catalog/game/1/file-12.zip")));
    }

    [Fact]
    public async Task RefusesYesWithoutAGroupAndAGroupItDoesntKnow()
    {
        await StoreAsync();

        CliResult yesAlone = await RunAsync("cleanup", "--yes");
        CliResult unknown = await RunAsync("cleanup", "--delete", "everything", "--yes");

        Assert.Equal((BadInput, BadInput), (yesAlone.ExitCode, unknown.ExitCode));
        Assert.Equal("--yes deletes the files of the group --delete names: archived, duplicates, unwatched or unmatched.", yesAlone.Errors.Trim());
        Assert.Contains("everything", unknown.Errors, StringComparison.Ordinal);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(_root, "fake-catalog"), "*", SearchOption.AllDirectories).Length);
    }

    private static string[] Lines(CliResult result) => result.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    [GeneratedRegex(" {2,}")]
    private static partial Regex Columns();

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(["library", .. arguments], Cancellation);

    private string InRoot(string relativePath) => LibraryPaths.Resolve(_root, relativePath);

    private async Task StoreAsync() => _root = await ClutteredLibrary.StoreAsync(_cli, Cancellation);
}
