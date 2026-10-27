using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>download</c> without <c>--to</c>: the file goes into the library, where its game's folder pattern says, and is
/// recorded there. Each command is a run of the app of its own.
/// </summary>
public sealed class DownloadCommandLibraryTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;
    private const int Waiting = 3;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(64 * 1024);

    private readonly TempFolder _folder = new();
    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public DownloadCommandLibraryTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeHandoffSource(_server.FileUrl)]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string ModFolder => Path.Combine(_cli.Downloads, "ModShelf", "fake-handoff", "game", $"{ModName} (mod)");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task SavesTheFileInTheLibraryAndRecordsIt()
    {
        CliResult result = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.Equal(Done, result.ExitCode);
        Assert.Contains($"Downloading {FileNameOf("3")} to {ModFolder}", result.Output, StringComparison.Ordinal);
        Assert.EndsWith("Added to the library.", result.Output.TrimEnd(), StringComparison.Ordinal);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder, FileNameOf("3")), Cancellation));
        string[] row = Assert.Single(Rows(await RunAsync("library", "list")));
        Assert.Equal(["mod", "Handoff", "Mod", "yes", "1"], row[1..6]);
    }

    [Fact]
    public async Task DoesntDownloadAFileTheLibraryHasAgain()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri);

        CliResult again = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.Equal(Done, again.ExitCode);
        Assert.Equal($"{Path.Combine(ModFolder, FileNameOf("3"))} is in the library already.", again.Output.Trim());
        Assert.Single(_server.Ranges);
    }

    [Fact]
    public async Task TheLinkPastedForAHandoffFindsTheDownload()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(["download", FileLink("1").AbsoluteUri], input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("1").AbsoluteUri);

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Contains("Added to the library.", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ModFolder, FileNameOf("1"))));
    }

    [Fact]
    public async Task DoesntUseAPastedLinkToAnotherFile()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(["download", FileLink("1").AbsoluteUri], input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("2", key: "key-for-file-2").AbsoluteUri);
        await run.WaitForOutputAsync($"That link is to another file, {FileNameOf("2")}.", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
        Assert.Contains("Cancelled", (await RunAsync("queue", "list")).QueueRowFor(FileNameOf("2")), StringComparison.Ordinal);
        Assert.Single(Rows(await RunAsync("library", "list")));
    }

    [Fact]
    public async Task APastedLinkFindsADownloadNamedWithItsFileId()
    {
        Directory.CreateDirectory(ModFolder);
        await File.WriteAllTextAsync(Path.Combine(ModFolder, FileNameOf("1")), "Someone else's file.", Cancellation);
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(["download", FileLink("1").AbsoluteUri], input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("1").AbsoluteUri);

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.DoesNotContain("another file", result.Output + result.Errors, StringComparison.Ordinal);
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder, "handoff-1 (1).zip"), Cancellation));
        Assert.Contains("Completed", (await RunAsync("queue", "list")).QueueRowFor("handoff-1"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsADownloadSavedUnderTheLibraryRootWithTo()
    {
        await RunAsync("download", HandoffLink("3").AbsoluteUri, "--to", ModFolder);

        // A run that downloads records what the library missed as it starts, while this one waits for its link.
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(["download", FileLink("1").AbsoluteUri], input);
        await run.WaitForOutputAsync("Link: ", Cancellation);
        string[]? row = null;
        for (int tries = 0; tries < 200 && row?[5] != "1"; tries++)
        {
            row = Rows(await RunAsync("library", "list")).SingleOrDefault();
            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        }

        await run.PressCtrlCAsync();
        await run.WaitForExitAsync(Cancellation);
        Assert.Equal("1", row?[5]);
    }

    [Fact]
    public async Task AWaitingDownloadKeepsItsPlaceWhenTheSettingsChange()
    {
        Assert.Equal(Waiting, (await RunAsync("download", FileLink("1").AbsoluteUri)).ExitCode);
        await RunAsync("settings", "set", "library.folders", "elsewhere/{file}");

        CliResult finished = await RunAsync("download", HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, finished.ExitCode);
        Assert.Contains($"Continuing the download of {FileNameOf("1")}", finished.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ModFolder, FileNameOf("1"))));
    }

    [Fact]
    public async Task GoesWhereTheSettingsSay()
    {
        string root = Path.Combine(_folder.Path, "Mods");
        await RunAsync("settings", "set", "library.root", root);
        await RunAsync("settings", "set", "library.folders.game", "{mod}/{file}");

        CliResult result = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.Equal(Done, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(root, ModName, FileNameOf("3"))));
    }

    [Fact]
    public async Task ANameNeedsAFolder()
    {
        CliResult refused = await RunAsync("download", HandoffLink("3").AbsoluteUri, "--name", "mine.zip");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal("--name works with --to only: files in the library keep the names their sites give them.", refused.Errors.Trim());
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task APlainWebLinkNeedsAFolder()
    {
        CliResult refused = await RunAsync("download", "https://elsewhere.example/files/mod.zip");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal(
            "Only the files of mods on sites such as Nexus Mods go into the library. To download this file into a folder "
            + "of your own, add --to <folder>.",
            refused.Errors.Trim());
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        CliResult refused = await RunAsync("download", "ftp://elsewhere.example/mod.zip");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal("No source can download that link.", refused.Errors.Trim());
    }

    [Fact]
    public async Task SaysWhenTheSettingsCantBeRead()
    {
        await File.WriteAllTextAsync(Path.Combine(_cli.Folder, "settings.json"), "{ not json", Cancellation);

        CliResult refused = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.StartsWith("The settings in", refused.Errors.Trim(), StringComparison.Ordinal);
        Assert.Empty(_server.Ranges);
    }

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

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
