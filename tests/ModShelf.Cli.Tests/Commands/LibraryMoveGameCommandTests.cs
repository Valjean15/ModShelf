using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library move-game</c>, on a library with a file of the fake site's game downloaded into it: what it would move,
/// then moving it with <c>--yes</c>, and back. Each command is a run of the app of its own.
/// </summary>
public sealed class LibraryMoveGameCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024);

    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public LibraryMoveGameCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeHandoffSource(_server.FileUrl)]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_cli.Downloads, "ModShelf");

    private string GameFolder => Path.Combine(_folder.Path, "Game mods");

    private static string FileIn(string folder) => Path.Combine(folder, "fake-handoff", "game", $"{ModName} (mod)", FileNameOf("3"));

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task SaysWhatItWouldMoveAndMovesNothing()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult said = await RunAsync("library", "move-game", "game", GameFolder);

        Assert.Equal(Done, said.ExitCode);
        string[] lines = said.Output.Trim().Split(Environment.NewLine);
        Assert.Equal($"fake-handoff/game's files move from {Root} to {GameFolder}: 1 file, {Cli.Commands.Sizes.Format(16 * 1024)}.", lines[0]);
        Assert.Equal("Nothing moved yet. To move them: add --yes.", lines[^1]);
        Assert.True(File.Exists(FileIn(Root)));
        Assert.False(Directory.Exists(GameFolder));
    }

    [Fact]
    public async Task MovesTheFilesWithYesAndTheGamesDownloadsGoThere()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult moved = await RunAsync("library", "move-game", "fake-handoff/game", GameFolder, "--yes");
        CliResult shown = await RunAsync("settings", "show");
        CliResult listed = await RunAsync("library", "list", "--files");
        CliResult again = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.Equal(Done, moved.ExitCode);
        Assert.EndsWith(
            $"Moved 1 file from {Root} to {GameFolder}.{Environment.NewLine}fake-handoff/game's files are in {GameFolder} now.",
            moved.Output.Trim(),
            StringComparison.Ordinal);
        Assert.Equal(Content, await File.ReadAllBytesAsync(FileIn(GameFolder), Cancellation));
        Assert.False(File.Exists(FileIn(Root)));
        Assert.Contains(shown.Output.Split(Environment.NewLine), line => line.StartsWith("games.fake-handoff/game.root", StringComparison.Ordinal) && line.Contains(GameFolder, StringComparison.Ordinal));
        Assert.Contains(FileIn(GameFolder), listed.Output, StringComparison.Ordinal);
        Assert.Equal($"{FileIn(GameFolder)} is in the library already.", again.Output.Trim());
    }

    [Fact]
    public async Task MovesThemBackIntoTheLibrarysFolder()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);
        await RunAsync("library", "move-game", "game", GameFolder, "--yes");

        CliResult back = await RunAsync("library", "move-game", "game", "--yes");

        Assert.Equal(Done, back.ExitCode);
        Assert.True(File.Exists(FileIn(Root)));
        Assert.DoesNotContain("games.fake-handoff/game.root", (await RunAsync("settings", "show")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheGameIsThereAlready()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult said = await RunAsync("library", "move-game", "game");

        Assert.Equal(Done, said.ExitCode);
        Assert.Equal($"fake-handoff/game's files are in {Root} already.", said.Output.Trim());
    }

    [Fact]
    public async Task RefusesAFolderInsideTheLibrary()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult refused = await RunAsync("library", "move-game", "game", Path.Combine(Root, "Game"), "--yes");

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal($"{Path.Combine(Root, "Game")} is inside the library's folder, {Root}. Choose a folder outside it.", refused.Errors.Trim());
        Assert.True(File.Exists(FileIn(Root)));
    }

    [Fact]
    public async Task RefusesAGameNamedBadly()
    {
        CliResult refused = await RunAsync("library", "move-game", "fake-handoff/", GameFolder);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("isn't a game", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingTheFolderOfAGameWithFilesPointsToMoveGame()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult refused = await RunAsync("settings", "set", "games.game.root", GameFolder);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal(
            $"fake-handoff/game has files in {Root}. Moving the game's files changes its folder and takes them along. To move them there: library move-game game \"{GameFolder}\"",
            refused.Errors.Trim());
    }

    [Fact]
    public async Task AGameWithoutFilesTakesAFolderBySetting()
    {
        CliResult set = await RunAsync("settings", "set", "games.fake-handoff/game.root", GameFolder);
        CliResult reset = await RunAsync("settings", "reset", "games.fake-handoff/game.root");

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal($"games.fake-handoff/game.root is now {GameFolder}: fake-handoff/game's files go there.", set.Output.Trim());
        Assert.Equal($"games.fake-handoff/game.root is gone, so fake-handoff/game's files go in the library's folder, {Root}.", reset.Output.Trim());
    }

    [Fact]
    public async Task RefusesAFolderThatIsntAFullPathBySetting()
    {
        CliResult refused = await RunAsync("settings", "set", "games.fake-handoff/game.root", "Game mods");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("'Game mods' isn't a full path", refused.Errors, StringComparison.Ordinal);
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
