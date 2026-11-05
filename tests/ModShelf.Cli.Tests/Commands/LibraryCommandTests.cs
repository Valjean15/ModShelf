using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library list</c>, <c>library watch</c>, <c>library unwatch</c> and <c>library forget</c> against a fake site. Each
/// command is a run of the app of its own, so what one stores, the next finds after a restart.
/// </summary>
public sealed class LibraryCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public LibraryCommandTests()
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
    public async Task AnEmptyLibrarySaysHowToWatchAMod()
    {
        CliResult listed = await ListAsync();

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal("The library is empty. To watch a mod for updates: library watch \"<its link>\"", listed.Output.Trim());
    }

    [Fact]
    public async Task WatchesAModAndListsIt()
    {
        CliResult watched = await RunAsync("watch", ModLink("1").AbsoluteUri);
        CliResult listed = await ListAsync();

        Assert.Equal(Done, watched.ExitCode);
        Assert.Equal("Watching Example Mod (mod 1 of game on Fake catalog site).", watched.Output.Trim());
        Assert.Contains("The library has 1 mod.", listed.Output, StringComparison.Ordinal);
        Assert.Equal([["game", "1", "Example", "Mod", "yes", "0", ModLink("1").AbsoluteUri]], Rows(listed));
    }

    [Fact]
    public async Task WatchingAFilesLinkWatchesItsMod()
    {
        await RunAsync("watch", FileLink("1", "20").AbsoluteUri);

        Assert.Equal("1", Assert.Single(Rows(await ListAsync()))[1]);
    }

    [Fact]
    public async Task WatchingAgainKeepsOneEntry()
    {
        await RunAsync("watch", ModLink("1").AbsoluteUri);
        CliResult again = await RunAsync("watch", ModLink("1").AbsoluteUri);

        Assert.Equal(Done, again.ExitCode);
        Assert.Single(Rows(await ListAsync()));
    }

    [Fact]
    public async Task ListsModsByGameThenName()
    {
        await RunAsync("watch", ModLink("2").AbsoluteUri);
        await RunAsync("watch", ModLink("1").AbsoluteUri);

        CliResult listed = await ListAsync();

        Assert.Contains("The library has 2 mods.", listed.Output, StringComparison.Ordinal);
        Assert.Equal(["1", "2"], Rows(listed).Select(row => row[1]));
    }

    [Fact]
    public async Task UnwatchingAModWithoutFilesTakesItOut()
    {
        await RunAsync("watch", ModLink("1").AbsoluteUri);

        CliResult unwatched = await RunAsync("unwatch", ModLink("1").AbsoluteUri);

        Assert.Equal(Done, unwatched.ExitCode);
        Assert.Equal("Stopped watching Example Mod. It had no downloaded files, so it's out of the library.", unwatched.Output.Trim());
        Assert.Empty(Rows(await ListAsync()));
    }

    [Fact]
    public async Task UnwatchingAModWithFilesKeepsIt()
    {
        await StoreAsync(1);

        CliResult unwatched = await RunAsync("unwatch", ModLink("1").AbsoluteUri);
        CliResult again = await RunAsync("unwatch", ModLink("1").AbsoluteUri);

        Assert.Equal("Stopped watching Example Mod. Its downloaded file stays in the library.", unwatched.Output.Trim());
        Assert.Equal("Example Mod isn't watched.", again.Output.Trim());
        Assert.Equal(["no", "1"], Assert.Single(Rows(await ListAsync()))[4..6]);
    }

    [Fact]
    public async Task CountsTheFilesThatStay()
    {
        await StoreAsync(2);

        CliResult unwatched = await RunAsync("unwatch", ModLink("1").AbsoluteUri);

        Assert.Equal("Stopped watching Example Mod. Its 2 downloaded files stay in the library.", unwatched.Output.Trim());
    }

    [Fact]
    public async Task SaysWhenAModIsntInTheLibrary()
    {
        CliResult unwatched = await RunAsync("unwatch", ModLink("1").AbsoluteUri);

        Assert.Equal(Done, unwatched.ExitCode);
        Assert.Equal("Mod 1 of game on Fake catalog site isn't in the library.", unwatched.Output.Trim());
    }

    [Theory]
    [InlineData(0, "Example Mod is out of the library. It had no downloaded files.")]
    [InlineData(1, "Example Mod is out of the library. Its downloaded file stays on disk.")]
    [InlineData(2, "Example Mod is out of the library. Its 2 downloaded files stay on disk.")]
    public async Task ForgetsAModWhoseFilesStay(int files, string message)
    {
        await StoreAsync(files);
        string[] onDisk = await PutFilesOnDiskAsync(files);

        CliResult forgot = await RunAsync("forget", ModLink("1").AbsoluteUri);

        Assert.Equal(Done, forgot.ExitCode);
        Assert.Equal(message, forgot.Output.Trim());
        Assert.Empty(Rows(await ListAsync()));
        Assert.All(onDisk, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task ListsTheFilesWithoutAHashTheyWereRecordedWithout()
    {
        await StoreAsync(2);
        await RunAsync("watch", FakeCatalogSite.ModLink("2").AbsoluteUri);

        CliResult listed = await _cli.RunAsync(["library", "list", "--files"], Cancellation);

        Assert.Equal(Done, listed.ExitCode);
        string[] lines = listed.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("The library has 2 files.", lines[^4]);
        Assert.Equal(
            [["game", "1", "file-1.zip", "-", "1", "B", "-"], ["game", "1", "file-2.zip", "-", "1", "B", "-"]],
            lines[^2..].Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[..7]));
    }

    [Fact]
    public async Task ListingTheFilesOfModsWithoutAnySaysSo()
    {
        await RunAsync("watch", ModLink("1").AbsoluteUri);

        CliResult listed = await _cli.RunAsync(["library", "list", "--files"], Cancellation);

        Assert.Equal("The library has no downloaded files.", listed.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n')[^1]);
    }

    [Fact]
    public async Task RescanReadsTheHashesTheLibraryLacksWithoutAskingTheSite()
    {
        await StoreAsync(2);
        await PutFilesOnDiskAsync(2);

        CliResult rescan = await RunAsync("rescan", ModLink("1").AbsoluteUri);
        CliResult again = await _cli.RunAsync(["library", "rescan"], Cancellation);

        Assert.Equal(Done, rescan.ExitCode);
        Assert.Equal("Rescanned 1 mod.\nRead the SHA-256 of 2 files.", rescan.Output.TrimEnd().ReplaceLineEndings("\n"));
        Assert.Equal(
            "Rescanned 1 mod.\nNothing new: every file in the mods' folders is in the library, and every file has its SHA-256.",
            again.Output.TrimEnd().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task RescanSaysWhichModsFilesCouldntBeMatchedAndFails()
    {
        ModRef broken = new(new GameRef(FakeCatalogSite.SourceId, "broken"), "7");
        string folder = await _cli.InAppAsync(
            async services =>
            {
                LibraryFile file = new(new ModFileRef(broken, "70"), "File 70", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, "fake-catalog/broken/7/file-70.zip", 3, DateTimeOffset.UnixEpoch);
                await services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                    broken, _ => LibraryMod.Watch(broken, "Broken Mod", null, null, DateTimeOffset.UnixEpoch).WithFile(file), Cancellation);
                string root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(Cancellation);
                return LibraryPaths.Resolve(root, "fake-catalog/broken/7");
            },
            Cancellation);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "file-70.zip"), "mod", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, "placed.zip"), "mod", Cancellation);

        CliResult rescan = await _cli.RunAsync(["library", "rescan"], Cancellation);

        Assert.Equal(Failed, rescan.ExitCode);
        Assert.Equal("Rescanned 1 mod.\nRead the SHA-256 of 1 file.", rescan.Output.TrimEnd().ReplaceLineEndings("\n"));
        Assert.Equal("Couldn't match the files of mod 7 of broken on Fake catalog site: Service unavailable.", rescan.Errors.Trim());
    }

    [Fact]
    public async Task SaysWhenAModToForgetIsntInTheLibrary()
    {
        CliResult forgot = await RunAsync("forget", ModLink("1").AbsoluteUri);

        Assert.Equal(Done, forgot.ExitCode);
        Assert.Equal("Mod 1 of game on Fake catalog site isn't in the library.", forgot.Output.Trim());
    }

    [Fact]
    public async Task SaysWhenTheSettingsCantBeReadToForget()
    {
        await StoreAsync(1);
        await File.WriteAllTextAsync(Path.Combine(_cli.Folder, "settings.json"), "{ not json", Cancellation);

        CliResult refused = await RunAsync("forget", ModLink("1").AbsoluteUri);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.StartsWith("The settings in", refused.Errors.Trim(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("watch", "not a link", BadInput, "isn't a complete link")]
    [InlineData("watch", "ftp://elsewhere.example/mod.zip", BadInput, "No source knows this link.")]
    [InlineData("watch", "https://elsewhere.example/files/mod.zip", BadInput, "isn't to a mod on a site where ModShelf can watch mods.")]
    [InlineData("watch", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("watch", "https://catalog.example/game/mods/401", Failed, "account link")]
    [InlineData("unwatch", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("forget", "not a link", BadInput, "isn't a complete link")]
    [InlineData("forget", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("rescan", "not a link", BadInput, "isn't a complete link")]
    [InlineData("rescan", "ftp://elsewhere.example/mod.zip", BadInput, "No source knows this link.")]
    public async Task SaysWhyItCantWatchOrUnwatch(string command, string link, int exitCode, string message)
    {
        CliResult refused = await RunAsync(command, link);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.Empty(Rows(await ListAsync()));
    }

    /// <summary>
    /// The table's rows, without its heading, split on spaces.
    /// </summary>
    private static string[][] Rows(CliResult result)
        =>
        [
            .. result.Output.ReplaceLineEndings("\n").Split('\n')
                .Where(line => line.StartsWith("game ", StringComparison.Ordinal))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
        ];

    private Task<CliResult> ListAsync() => _cli.RunAsync(["library", "list"], Cancellation);

    private Task<CliResult> RunAsync(string command, string link) => _cli.RunAsync(["library", command, link], Cancellation);

    /// <summary>
    /// Puts the files <see cref="StoreAsync"/> stored on disk, where the library has them.
    /// </summary>
    private Task<string[]> PutFilesOnDiskAsync(int files) => _cli.InAppAsync(
        async services =>
        {
            string root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(Cancellation);
            string[] paths =
            [
                .. Enumerable.Range(1, files).Select(file => LibraryPaths.Resolve(
                    root, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"fake-catalog/game/1/file-{file}.zip"))),
            ];
            foreach (string path in paths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "mod", Cancellation);
            }

            return paths;
        },
        Cancellation);

    /// <summary>
    /// Puts mod 1 in the library, watched, with <paramref name="files"/> downloaded files, as a download would.
    /// </summary>
    private Task<LibraryChange> StoreAsync(int files) => _cli.InAppAsync(
        async services =>
        {
            ModRef mod = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");
            LibraryMod stored = LibraryMod.Watch(mod, "Example Mod", "Someone", ModLink("1"), DateTimeOffset.UnixEpoch);
            for (int file = 1; file <= files; file++)
            {
                string id = file.ToString(System.Globalization.CultureInfo.InvariantCulture);
                stored = stored.WithFile(new LibraryFile(
                    new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, $"fake-catalog/game/1/file-{id}.zip", 1, DateTimeOffset.UnixEpoch));
            }

            return await services.GetRequiredService<ILibraryRepository>().UpdateAsync(mod, _ => stored, Cancellation);
        },
        Cancellation);
}
