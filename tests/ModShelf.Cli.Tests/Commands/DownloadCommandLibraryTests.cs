using System.Security.Cryptography;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library.Ports;
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
    private const int Interrupted = 130;

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
        Assert.DoesNotContain(FileNameOf("2"), (await RunAsync("queue", "list")).Output, StringComparison.Ordinal);
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
    public async Task SaysWhatHappenedToTheOldVersions()
    {
        await RunAsync("download", HandoffLink("1").AbsoluteUri);
        CliResult second = await RunAsync("download", HandoffLink("2").AbsoluteUri);

        CliResult third = await RunAsync("download", HandoffLink("3").AbsoluteUri);

        Assert.DoesNotContain("old version", second.Output, StringComparison.Ordinal);
        Assert.EndsWith($"Added to the library.{Environment.NewLine}Deleted the old version {FileNameOf("1")}.", third.Output.TrimEnd(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(ModFolder, FileNameOf("1"))));
        Assert.Equal("2", Assert.Single(Rows(await RunAsync("library", "list")))[5]);
    }

    [Fact]
    public async Task ArchivesTheOldVersionsWhenTheSettingsSay()
    {
        await RunAsync("settings", "set", "library.oldVersions", "archive");
        await RunAsync("download", HandoffLink("1").AbsoluteUri);

        CliResult second = await RunAsync("download", HandoffLink("2").AbsoluteUri);

        Assert.EndsWith($"Moved the old version {FileNameOf("1")} to the _archive folder in its folder.", second.Output.TrimEnd(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(ModFolder, "_archive", FileNameOf("1"))));
    }

    [Fact]
    public async Task ADownloadTheLibraryCantRecordIsSaidToBeSaved()
    {
        await using InProcessCli cli = WithLibraryChanges(_ => throw new SqliteException("SQLite Error 5: 'database is locked'.", 5));

        CliResult result = await cli.RunAsync(["download", HandoffLink("3").AbsoluteUri], Cancellation);

        Assert.Equal(Failed, result.ExitCode);
        Assert.Contains($"Saved {Path.Combine(ModFolderIn(cli), FileNameOf("3"))}", result.Output, StringComparison.Ordinal);
        Assert.Equal("The file is saved, but it couldn't be added to the library: SQLite Error 5: 'database is locked'.", result.Errors.Trim());
    }

    [Fact]
    public async Task CtrlCWhileTheDownloadIsRecordedSaysTheFileIsSaved()
    {
        TaskCompletionSource recording = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using InProcessCli cli = WithLibraryChanges(async cancellationToken =>
        {
            recording.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        await using CliRun run = cli.Start(["download", HandoffLink("3").AbsoluteUri], TextReader.Null);
        await run.WaitForOutputAsync("Saved ", Cancellation);
        await recording.Task.WaitAsync(TimeSpan.FromSeconds(60), Cancellation);

        await run.PressCtrlCAsync();
        CliResult stopped = await run.WaitForExitAsync(Cancellation);

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Equal(
            $"Stopped. {Path.Combine(ModFolderIn(cli), FileNameOf("3"))} is saved; it's added to the library the next time a download command runs, or the desktop app starts.",
            stopped.Errors.Trim());
    }

    [Fact]
    public async Task ADownloadThatFailsUnexpectedlyPointsToTheLog()
    {
        string folder = Path.Combine(_folder.Path, "Elsewhere");

        CliResult failed = await RunAsync("download", HandoffLink("crash").AbsoluteUri, "--to", folder);

        Assert.Equal(Failed, failed.ExitCode);
        Assert.Equal($"The download failed unexpectedly. The details are in the log in {Path.Combine(_cli.Folder, "logs")}.", failed.Errors.Trim());
        Assert.Contains(_cli.Logs.Entries, entry => entry.Contains("The site's answer made no sense.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADownloadTheSitesRateLimitStoppedSaysSo()
    {
        string folder = Path.Combine(_folder.Path, "Elsewhere");

        CliResult failed = await RunAsync("download", HandoffLink("busy").AbsoluteUri, "--to", folder);

        Assert.Equal(Failed, failed.ExitCode);
        Assert.Equal(
            "The download failed: the site's rate limit for this account is used up. Run the same command again once it resets; account check says when.",
            failed.Errors.Trim());
    }

    [Fact]
    public async Task AFolderThatsAFileIsRefusedBeforeAnythingIsQueued()
    {
        string file = Path.Combine(_folder.Path, "notes.txt");
        await File.WriteAllTextAsync(file, "not a folder", Cancellation);

        CliResult refused = await RunAsync("download", _server.FileUrl.AbsoluteUri, "--to", file);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal($"{file} is a file. Give --to a folder; it's created if it doesn't exist.", refused.Errors.Trim());
        Assert.Contains("The queue is empty.", (await RunAsync("queue", "list")).Output, StringComparison.Ordinal);
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

    private static string ModFolderIn(InProcessCli cli) => Path.Combine(cli.Downloads, "ModShelf", "fake-handoff", "game", $"{ModName} (mod)");

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);

    /// <summary>
    /// The CLI, with each change to the library first doing what <paramref name="beforeChange"/> says.
    /// </summary>
    private InProcessCli WithLibraryChanges(Func<CancellationToken, Task> beforeChange)
        => new(_site, _browser, sources: [new FakeHandoffSource(_server.FileUrl)], configureServices: services =>
        {
            ServiceDescriptor database = services.Last(service => service.ServiceType == typeof(ILibraryRepository));
            services.Remove(database);
            services.AddSingleton<ILibraryRepository>(provider => new ScriptedLibraryRepository(
                (ILibraryRepository)ActivatorUtilities.CreateInstance(provider, database.ImplementationType!),
                beforeChange));
        });
}
