using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>library check</c> and <c>library updates</c>. The handoff site's files 1 to 3 are each the update of the one
/// before; the catalog site has a feed. Each command is a run of the app of its own.
/// </summary>
public sealed class LibraryUpdatesCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(4 * 1024);

    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public LibraryUpdatesCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeHandoffSource(_server.FileUrl), new FakeCatalogSite()]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task SaysWhenNoModIsWatched()
    {
        CliResult checkedNothing = await RunAsync("library", "check");
        CliResult none = await RunAsync("library", "updates");

        Assert.Equal((Done, Done), (checkedNothing.ExitCode, none.ExitCode));
        Assert.Equal("No mods are watched. To watch one: library watch \"<its link>\"", checkedNothing.Output.Trim());
        Assert.Equal("There are no updates. To check the watched mods now: library check", none.Output.Trim());
    }

    [Fact]
    public async Task FindsTheUpdateOfADownloadedFile()
    {
        await RunAsync("download", HandoffLink("1").AbsoluteUri);

        CliResult check = await RunAsync("library", "check");
        CliResult again = await RunAsync("library", "check");

        Assert.Equal(Done, check.ExitCode);
        string[] lines = Lines(check);
        Assert.Equal(["Checked 1 mod with 1 request.", "1 new update:"], lines[..2]);
        Assert.Equal(["Handoff", "Mod", "File", "3", "1.3"], Words(lines[3])[..5]);
        Assert.EndsWith($"{FileNameOf("1")}  {FileLink("3").AbsoluteUri}", lines[3], StringComparison.Ordinal);
        Assert.Equal("To see every update: library updates", lines[4]);
        Assert.Equal(["Checked 1 mod with 1 request.", "No new updates."], Lines(again));
    }

    [Fact]
    public async Task ListsTheUpdatesWithTheirChanges()
    {
        await RunAsync("download", HandoffLink("1").AbsoluteUri);
        await RunAsync("library", "check");

        CliResult plain = await RunAsync("library", "updates");
        CliResult listed = await RunAsync("library", "updates", "--changelog");

        Assert.Equal((Done, Done), (plain.ExitCode, listed.ExitCode));
        Assert.Equal(["To download one: download \"<its link>\""], Lines(plain)[3..]);
        string[] lines = Lines(listed);
        Assert.Equal("1 update for 1 mod:", lines[0]);
        Assert.Equal(["Mod", "Update", "Version", "Uploaded", "Replaces", "Link"], Words(lines[1]));
        Assert.Contains(FileLink("3").AbsoluteUri, lines[2], StringComparison.Ordinal);
        Assert.Equal(["To download one: download \"<its link>\"", "", "File 3 (1.3):", "  - Faster.", "  - Fixed the crash."], lines[3..]);
    }

    [Fact]
    public async Task ListsTheChangesOfEachUpdateOfAMod()
    {
        await RunAsync("download", HandoffLink("1").AbsoluteUri);
        ModRef mod = new(new GameRef(FakeHandoffSource.SourceId, "game"), "mod");
        await _cli.InAppAsync(
            services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                mod, stored => stored!.WithUpdates([Update(mod, "2", days: 2), Update(mod, "3", days: 3)]), Cancellation),
            Cancellation);

        CliResult listed = await RunAsync("library", "updates", "--changelog");

        Assert.Equal(["File 3 (1.3):", "  - Faster.", "  - Fixed the crash.", "", "File 2 (1.2):", "  - Smaller."], Lines(listed)[^6..]);
    }

    [Fact]
    public async Task DownloadingTheUpdateTakesItOff()
    {
        await RunAsync("download", HandoffLink("1").AbsoluteUri);
        await RunAsync("library", "check");

        await RunAsync("download", HandoffLink("3").AbsoluteUri);
        CliResult listed = await RunAsync("library", "updates");

        Assert.StartsWith("There are no updates. The watched mods were checked ", listed.Output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskTheFeedOnceTheModsWereChecked()
    {
        await RunAsync("library", "watch", FakeCatalogSite.ModLink("1").AbsoluteUri);
        CliResult first = await RunAsync("library", "check");

        CliResult second = await RunAsync("library", "check");

        Assert.Equal("Checked 1 mod with 1 request.", Lines(first)[0]);
        Assert.Equal("Checked 1 mod with 1 request.", Lines(second)[0]);
    }

    [Fact]
    public async Task SaysWhatCouldntBeChecked()
    {
        await StoreAsync(new ModRef(new GameRef(FakeCatalogSite.SourceId, "broken"), "7"), DateTimeOffset.UtcNow.AddHours(-1));

        CliResult check = await RunAsync("library", "check");

        Assert.Equal(Failed, check.ExitCode);
        Assert.Equal("Checked 0 mods with 1 request.", Lines(check)[0]);
        Assert.Equal("Couldn't check broken on fake-catalog: Service unavailable.", check.Errors.Trim());
    }

    [Fact]
    public async Task SetsHowOftenToCheck()
    {
        CliResult set = await RunAsync("settings", "set", "updates.interval", "30M");
        CliResult shown = await RunAsync("settings", "show");
        CliResult reset = await RunAsync("settings", "reset", "updates.interval");
        CliResult refused = await RunAsync("settings", "set", "updates.interval", "5m");

        Assert.Equal("updates.interval is now 30m.", set.Output.Trim());
        Assert.Contains(Lines(shown), line => Words(line) is ["updates.interval", "30m"]);
        Assert.Equal("updates.interval is back to its default, 6h.", reset.Output.Trim());
        Assert.Equal(BadInput, refused.ExitCode);
        Assert.StartsWith("'5m' isn't an interval.", refused.Errors.Trim(), StringComparison.Ordinal);
    }

    private static string[] Lines(CliResult result) => result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    private static LibraryUpdate Update(ModRef mod, string id, int days) => new(
        new ModFileRef(mod, id),
        $"File {id}",
        FileNameOf(id),
        $"1.{id}",
        ModFileCategory.Main,
        DateTimeOffset.UnixEpoch.AddDays(days),
        new ModFileRef(mod, "1"),
        DateTimeOffset.UnixEpoch.AddDays(10),
        FileLink(id));

    private static string[] Words(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private Task<LibraryChange> StoreAsync(ModRef mod, DateTimeOffset checkedAt) => _cli.InAppAsync(
        services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
            mod, _ => LibraryMod.Watch(mod, "Broken Mod", null, null, checkedAt.AddDays(-1)).Checked(checkedAt), Cancellation),
        Cancellation);

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
