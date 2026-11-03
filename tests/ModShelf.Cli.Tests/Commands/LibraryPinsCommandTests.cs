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
/// <c>library pin</c>, <c>library unpin</c>, <c>library skip</c> and <c>library note</c>, and what <c>library list</c> and
/// <c>library updates</c> say of them. Mod 1 has file 1 at version 1.1, and update 2, at 1.2, which replaces it.
/// </summary>
public sealed class LibraryPinsCommandTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly ModRef Mod = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public LibraryPinsCommandTests()
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
    public async Task PinsAndUnpinsAModAndTheListSaysSo()
    {
        await StoreAsync();

        CliResult pinned = await RunAsync("library", "pin", ModLink("1").AbsoluteUri);
        CliResult again = await RunAsync("library", "pin", ModLink("1").AbsoluteUri);
        CliResult listed = await RunAsync("library", "list");
        CliResult unpinned = await RunAsync("library", "unpin", FileLink("1", "1").AbsoluteUri);
        CliResult unpinnedAgain = await RunAsync("library", "unpin", ModLink("1").AbsoluteUri);

        Assert.Equal(
            (Done, "Pinned Example Mod at 1.1: its updates are still found, but aren't offered. To unpin it: library unpin \"<its link>\""),
            (pinned.ExitCode, pinned.Output.Trim()));
        Assert.Equal("Example Mod is pinned already at 1.1.", again.Output.Trim());
        Assert.Contains(listed.Output.ReplaceLineEndings("\n").Split('\n'), line => line.StartsWith("game ", StringComparison.Ordinal) && line.Contains(" yes, pinned ", StringComparison.Ordinal));
        Assert.Equal((Done, "Unpinned Example Mod: its updates are offered again."), (unpinned.ExitCode, unpinned.Output.Trim()));
        Assert.Equal("Example Mod isn't pinned.", unpinnedAgain.Output.Trim());
    }

    [Fact]
    public async Task APinnedModIsAtItsMainFilesVersionNotAnOptionalFiles()
    {
        await StoreAsync();
        await _cli.InAppAsync(
            services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                Mod,
                stored => stored!
                    .WithFile(new LibraryFile(
                        new ModFileRef(Mod, "4"), "Fonts", "0.9", ModFileCategory.Optional, DateTimeOffset.UnixEpoch.AddDays(-9), "fake-catalog/game/1/fonts.zip", 1, DateTimeOffset.UnixEpoch))
                    .WithFile(new LibraryFile(
                        new ModFileRef(Mod, "5"), "Extras", "2.0", ModFileCategory.Optional, DateTimeOffset.UnixEpoch.AddDays(9), "fake-catalog/game/1/extras.zip", 1, DateTimeOffset.UnixEpoch)),
                Cancellation),
            Cancellation);

        CliResult pinned = await RunAsync("library", "pin", ModLink("1").AbsoluteUri);

        Assert.StartsWith("Pinned Example Mod at 1.1:", pinned.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APinnedModsUpdatesAreLeftOutOfTheUpdates()
    {
        await StoreAsync();
        await RunAsync("library", "pin", ModLink("1").AbsoluteUri);

        CliResult updates = await RunAsync("library", "updates");

        Assert.Equal(Done, updates.ExitCode);
        Assert.Equal(
            [
                "There are no updates. To check the watched mods now: library check",
                "Left out: 1 update of pinned mods. To offer them: library unpin \"<the mod's link>\"",
            ],
            Lines(updates));
    }

    [Fact]
    public async Task SkipsAnUpdateByItsLinkAndOffersItAgain()
    {
        await StoreAsync();

        CliResult skipped = await RunAsync("library", "skip", FileLink("1", "2").AbsoluteUri);
        CliResult again = await RunAsync("library", "skip", FileLink("1", "2").AbsoluteUri);
        CliResult updates = await RunAsync("library", "updates");
        CliResult offered = await RunAsync("library", "skip", FileLink("1", "2").AbsoluteUri, "--undo");
        CliResult offeredAgain = await RunAsync("library", "skip", FileLink("1", "2").AbsoluteUri, "--undo");

        Assert.Equal(
            (Done, "Skipped File 2 (1.2) of Example Mod until a newer update comes. To offer it again: library skip \"<its link>\" --undo"),
            (skipped.ExitCode, skipped.Output.Trim()));
        Assert.Equal("That update of Example Mod is skipped already.", again.Output.Trim());
        Assert.Equal("Left out: 1 skipped update. To offer one again: library skip \"<its link>\" --undo", Lines(updates)[^1]);
        Assert.StartsWith("There are no updates.", updates.Output, StringComparison.Ordinal);
        Assert.Equal((Done, "File 2 (1.2) of Example Mod is offered again."), (offered.ExitCode, offered.Output.Trim()));
        Assert.Equal("That update of Example Mod isn't skipped.", offeredAgain.Output.Trim());
        Assert.Contains("1 update for 1 mod:", (await RunAsync("library", "updates")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkippingAFileThatIsntAnUpdateIsRefused()
    {
        await StoreAsync();

        CliResult refused = await RunAsync("library", "skip", FileLink("1", "1").AbsoluteUri);

        Assert.Equal((BadInput, "File 1 isn't an update of the mod."), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Fact]
    public async Task KeepsANoteThatTheListShowsAndClearsIt()
    {
        await StoreAsync();

        CliResult noted = await RunAsync("library", "note", ModLink("1").AbsoluteUri, "  Stay on 1.1: 1.2 breaks saves.  ");
        CliResult listed = await RunAsync("library", "list");
        CliResult cleared = await RunAsync("library", "note", ModLink("1").AbsoluteUri, "--clear");
        CliResult clearedAgain = await RunAsync("library", "note", ModLink("1").AbsoluteUri, "--clear");

        Assert.Equal((Done, "Kept the note of Example Mod."), (noted.ExitCode, noted.Output.Trim()));
        Assert.Equal(["Notes:", "Example Mod (game 1): Stay on 1.1: 1.2 breaks saves."], Lines(listed)[^2..]);
        Assert.Equal("Removed the note of Example Mod.", cleared.Output.Trim());
        Assert.Equal("Example Mod had no note.", clearedAgain.Output.Trim());
        Assert.DoesNotContain("Notes:", (await RunAsync("library", "list")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANoteOnSeveralLinesIsListedOnOne()
    {
        await StoreAsync();
        await RunAsync("library", "note", ModLink("1").AbsoluteUri, "First line\nSecond line");

        Assert.Equal("Example Mod (game 1): First line / Second line", Lines(await RunAsync("library", "list"))[^1]);
    }

    [Theory]
    [InlineData(new[] { "--clear", "A note." }, "Give a note or --clear, not both.")]
    [InlineData(new string[0], "Give the note, or --clear to remove the one the mod has.")]
    [InlineData(new[] { " " }, "Give the note, or --clear to remove the one the mod has.")]
    public async Task ANoteNeedsTextOrClearButNotBoth(string[] rest, string message)
    {
        await StoreAsync();

        CliResult refused = await RunAsync(["library", "note", ModLink("1").AbsoluteUri, .. rest]);

        Assert.Equal((BadInput, message), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Fact]
    public async Task ANoteTooLongIsRefused()
    {
        await StoreAsync();

        CliResult refused = await RunAsync("library", "note", ModLink("1").AbsoluteUri, new string('a', 2001));

        Assert.Equal((BadInput, "A note holds at most 2,000 characters; this one has 2,001."), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("unpin")]
    [InlineData("skip")]
    public async Task SaysWhenTheModIsntInTheLibrary(string command)
    {
        CliResult refused = await RunAsync("library", command, FileLink("1", "2").AbsoluteUri);

        Assert.Equal((Failed, "Mod 1 of game on fake-catalog isn't in the library."), (refused.ExitCode, refused.Errors.Trim()));
    }

    [Fact]
    public async Task SaysWhenALinkIsntOne()
    {
        CliResult refused = await RunAsync("library", "pin", "not a link");

        Assert.Equal(BadInput, refused.ExitCode);
    }

    private static string[] Lines(CliResult result)
        => result.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);

    /// <summary>
    /// Puts mod 1 in the library, watched, with file 1 and update 2, as a download and a check would.
    /// </summary>
    private Task<LibraryChange> StoreAsync() => _cli.InAppAsync(
        services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
            Mod,
            _ => LibraryMod.Watch(Mod, "Example Mod", "Someone", ModLink("1"), DateTimeOffset.UnixEpoch)
                .WithFile(new LibraryFile(
                    new ModFileRef(Mod, "1"), "File 1", "1.1", ModFileCategory.Main, DateTimeOffset.UnixEpoch, "fake-catalog/game/1/file-1.zip", 1, DateTimeOffset.UnixEpoch))
                .WithUpdates(
                [
                    new LibraryUpdate(
                        new ModFileRef(Mod, "2"),
                        "File 2",
                        "file-2.zip",
                        "1.2",
                        ModFileCategory.Main,
                        DateTimeOffset.UnixEpoch.AddDays(1),
                        new ModFileRef(Mod, "1"),
                        DateTimeOffset.UnixEpoch.AddDays(1),
                        FileLink("1", "2")),
                ]),
            Cancellation),
        Cancellation);
}
