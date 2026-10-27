using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library list</c>, <c>library watch</c> and <c>library unwatch</c> against a fake site. Each command is a run of
/// the app of its own, so what one stores, the next finds after a restart.
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
        Assert.Equal("Watching Example Mod (mod 1 of game on fake-catalog).", watched.Output.Trim());
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
        Assert.Equal("Mod 1 of game on fake-catalog isn't in the library.", unwatched.Output.Trim());
    }

    [Theory]
    [InlineData("watch", "not a link", BadInput, "isn't a complete link")]
    [InlineData("watch", "ftp://elsewhere.example/mod.zip", BadInput, "No source knows this link.")]
    [InlineData("watch", "https://elsewhere.example/files/mod.zip", BadInput, "isn't to a mod on a site where ModShelf can watch mods.")]
    [InlineData("watch", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
    [InlineData("watch", "https://catalog.example/game/mods/401", Failed, "account link")]
    [InlineData("unwatch", "https://catalog.example/game/mods/404", BadInput, "The site has no such mod or file.")]
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
