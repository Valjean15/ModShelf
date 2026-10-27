using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library move</c>, on a library with files downloaded into it. Each command is a run of the app of its own.
/// </summary>
public sealed class LibraryMoveCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024);

    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public LibraryMoveCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeHandoffSource(_server.FileUrl)]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string OldRoot => Path.Combine(_cli.Downloads, "ModShelf");

    private string NewRoot => Path.Combine(_folder.Path, "My Mods");

    private static string ModFolder(string root) => Path.Combine(root, "fake-handoff", "game", $"{ModName} (mod)");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task MovesTheFilesAndTheRoot()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult moved = await RunAsync("library", "move", NewRoot);

        Assert.Equal(Done, moved.ExitCode);
        Assert.Equal(
            $"Moved 1 file from {OldRoot} to {NewRoot}.{Environment.NewLine}The library is in {NewRoot} now.",
            moved.Output.Trim());
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder(NewRoot), FileNameOf("3")), Cancellation));
        Assert.False(Directory.Exists(OldRoot));
        CliResult again = await RunAsync("download", HandoffLink("3").AbsoluteUri);
        Assert.Equal($"{Path.Combine(ModFolder(NewRoot), FileNameOf("3"))} is in the library already.", again.Output.Trim());
    }

    [Fact]
    public async Task LeavesTheUsersOwnFiles()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);
        string notes = Path.Combine(OldRoot, "notes.txt");
        await File.WriteAllTextAsync(notes, "Mine.", Cancellation);

        await RunAsync("library", "move", NewRoot);

        Assert.True(File.Exists(notes));
        Assert.False(Directory.Exists(ModFolder(OldRoot)));
    }

    [Fact]
    public async Task SaysWhenTheLibraryIsThereAlready()
    {
        CliResult moved = await RunAsync("library", "move", OldRoot);

        Assert.Equal(Done, moved.ExitCode);
        Assert.Equal($"The library is in {OldRoot} already.", moved.Output.Trim());
    }

    [Fact]
    public async Task WaitsForDownloadsIntoTheLibrary()
    {
        await RunAsync("download", FileLink("1").AbsoluteUri);

        CliResult refused = await RunAsync("library", "move", NewRoot);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal($"A download into {OldRoot} isn't finished. Let it finish, or cancel it, first.", refused.Errors.Trim());
    }

    [Fact]
    public async Task RefusesAFolderInsideTheLibrary()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult refused = await RunAsync("library", "move", Path.Combine(OldRoot, "Inner"));

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Contains("is inside the library", refused.Errors, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ModFolder(OldRoot), FileNameOf("3"))));
    }

    [Fact]
    public async Task NeverMovesOverAnotherFile()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);
        Directory.CreateDirectory(ModFolder(NewRoot));
        await File.WriteAllTextAsync(Path.Combine(ModFolder(NewRoot), FileNameOf("3")), "Someone else's.", Cancellation);

        CliResult refused = await RunAsync("library", "move", NewRoot);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.StartsWith($"A file is at {Path.Combine(ModFolder(NewRoot), FileNameOf("3"))}", refused.Errors.Trim(), StringComparison.Ordinal);
        Assert.Equal("Someone else's.", await File.ReadAllTextAsync(Path.Combine(ModFolder(NewRoot), FileNameOf("3")), Cancellation));
    }

    [Fact]
    public async Task SettingTheRootOfALibraryWithFilesPointsToMove()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult refused = await RunAsync("settings", "set", "library.root", NewRoot);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal(
            $"The library has files in {OldRoot}. Moving the library changes its root and takes them along. To move it there: library move \"{NewRoot}\"",
            refused.Errors.Trim());
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
