using System.Security.Cryptography;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>download</c> with a site that wants each download started on its website, as Nexus Mods does for free accounts:
/// the command shows the page, asks for the link the site hands over, and carries on once it's pasted.
/// </summary>
public sealed class DownloadCommandHandoffTests : IAsyncDisposable
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
    private readonly FakeHandoffSource _source;
    private readonly InProcessCli _cli;

    public DownloadCommandHandoffTests()
    {
        _source = new FakeHandoffSource(_server.FileUrl);
        _cli = new InProcessCli(_site, _browser, sources: [_source]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Downloads => Path.Combine(_folder.Path, "Downloads");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public async Task ShowsThePageAndDownloadsOnceTheLinkIsPasted()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("1").AbsoluteUri);

        CliResult result = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, result.ExitCode);
        Assert.Contains(Page("1").AbsoluteUri, result.Output, StringComparison.Ordinal);
        Assert.Contains($"Downloading {FileNameOf("1")}", result.Output, StringComparison.Ordinal);
        Assert.Equal([Page("1")], _browser.Opened);
        Assert.Equal(Content, await File.ReadAllBytesAsync(SavedFile("1"), Cancellation));
        Uri?[] handoffs = [null, HandoffLink("1")];
        Assert.Equal(handoffs, _source.Handoffs);
        AssertKeyNeverShown(result);
    }

    [Fact]
    public async Task AsksAgainWhenTheSiteRefusesTheLink()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("1", key: "expired-key").AbsoluteUri);
        await run.WaitForOutputAsync("didn't take that link", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
        Assert.Equal([Page("1"), Page("1")], _browser.Opened);
        Assert.Equal(HandoffLink("1"), _source.Handoffs[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("the key, I think")]
    public async Task AsksAgainForALineThatIsntALink(string line)
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(line);
        await run.WaitForOutputAsync("That isn't a link.", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
    }

    [Fact]
    public async Task AsksAgainForALinkNoSourceKnows()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type("mailto:someone@example.com");
        await run.WaitForOutputAsync("No source can download that link.", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
    }

    [Fact]
    public async Task DoesntUseALinkToAnotherFile()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(HandoffLink("2", key: "key-for-file-2").AbsoluteUri);
        await run.WaitForOutputAsync($"That link is to another file, {FileNameOf("2")}.", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
        Assert.DoesNotContain(FileNameOf("2"), (await RunAsync(["queue", "list"])).Output, StringComparison.Ordinal);
        Assert.False(File.Exists(SavedFile("2")));
    }

    [Fact]
    public async Task SaysTheFilesOwnPageIsntTheLinkToPaste()
    {
        // The page the CLI shows, pasted back: it's the file's, but the site's link comes once the download starts there.
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Type(FileLink("1").AbsoluteUri);
        await run.WaitForOutputAsync($"That's a link to {FileNameOf("1")}, but not the one the site hands over", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        Assert.Equal(Done, (await run.WaitForExitAsync(Cancellation)).ExitCode);
        Assert.True(File.Exists(SavedFile("1")));
    }

    [Fact]
    public async Task CtrlCLeavesTheDownloadWaitingAndItsLinkFinishesItLater()
    {
        CliResult stopped;
        using (ControlledTextReader input = new())
        {
            await using CliRun run = _cli.Start(Download(FileLink("1")), input);
            await run.WaitForOutputAsync("Link: ", Cancellation);
            await run.PressCtrlCAsync();
            stopped = await run.WaitForExitAsync(Cancellation);
        }

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Contains("still waiting to be started on the site", stopped.Errors, StringComparison.Ordinal);
        Assert.Contains("AwaitingHandoff", (await RunAsync(["queue", "list"])).QueueRowFor(FileNameOf("1")), StringComparison.Ordinal);

        CliResult finished = await RunAsync(Download(HandoffLink("1")));

        Assert.Equal(Done, finished.ExitCode);
        Assert.Contains($"Continuing the download of {FileNameOf("1")}", finished.Output, StringComparison.Ordinal);
        Assert.Equal(Content, await File.ReadAllBytesAsync(SavedFile("1"), Cancellation));
        AssertKeyNeverShown(finished);
    }

    [Fact]
    public async Task TheSameCommandShowsThePageAgainAfterARestart()
    {
        CliResult first = await RunAsync(Download(FileLink("1")));
        Assert.Equal(Waiting, first.ExitCode);

        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);
        input.Type(HandoffLink("1").AbsoluteUri);

        CliResult second = await run.WaitForExitAsync(Cancellation);
        Assert.Equal(Done, second.ExitCode);
        Assert.Contains($"Continuing the download of {FileNameOf("1")}", second.Output, StringComparison.Ordinal);
        Assert.Contains(Page("1").AbsoluteUri, second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandoffLinkAloneDownloadsTheFile()
    {
        CliResult result = await RunAsync(Download(HandoffLink("3")));

        Assert.Equal(Done, result.ExitCode);
        Assert.Equal(Content, await File.ReadAllBytesAsync(SavedFile("3"), Cancellation));
        Assert.Equal([HandoffLink("3")], _source.Handoffs);
        Assert.Empty(_browser.Opened);
        AssertKeyNeverShown(result);
    }

    [Fact]
    public async Task CtrlCThatEndsTheReadFirstStillStops()
    {
        // On Windows, Ctrl+C at the prompt ends the read before it stops the command.
        ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(FileLink("1")), input);
        await run.WaitForOutputAsync("Link: ", Cancellation);

        input.Dispose();
        await run.PressCtrlCAsync();
        CliResult stopped = await run.WaitForExitAsync(Cancellation);

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Contains("still waiting to be started on the site", stopped.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlCWhileTheLinkIsLookedUpSaysNothingWasAdded()
    {
        using ControlledTextReader input = new();
        await using CliRun run = _cli.Start(Download(new Uri("https://handoff.example/slow/1")), input);
        await _source.SlowLinkAsked.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        await run.PressCtrlCAsync();
        CliResult stopped = await run.WaitForExitAsync(Cancellation);

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Equal("Stopped before anything was added.", stopped.Errors.Trim());
        Assert.Contains("The queue is empty.", (await RunAsync(["queue", "list"])).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsWaitingWhenTheInputEndsWithoutALink()
    {
        CliResult result = await RunAsync(Download(FileLink("1")));

        Assert.Equal(Waiting, result.ExitCode);
        Assert.Contains("No link was given", result.Errors, StringComparison.Ordinal);
        Assert.Contains("AwaitingHandoff", (await RunAsync(["queue", "list"])).QueueRowFor(FileNameOf("1")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSiteWantsAnAccountToLookTheLinkUp()
    {
        CliResult result = await RunAsync(Download(new Uri("https://handoff.example/locked/1")));

        Assert.Equal(Failed, result.ExitCode);
        Assert.Contains("account link", result.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSiteHasNoSuchFile()
    {
        CliResult result = await RunAsync(Download(new Uri("https://handoff.example/missing/1")));

        Assert.Equal(BadInput, result.ExitCode);
        Assert.Contains("no such mod or file", result.Errors, StringComparison.Ordinal);
    }

    private string[] Download(Uri link) => ["download", link.AbsoluteUri, "--to", Downloads];

    private string SavedFile(string fileId) => Path.Combine(Downloads, FileNameOf(fileId));

    private Task<CliResult> RunAsync(string[] arguments) => _cli.RunAsync(arguments, Cancellation);

    private void AssertKeyNeverShown(CliResult result)
    {
        Assert.DoesNotContain(GoodKey, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(GoodKey, result.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain(_cli.Logs.Entries, entry => entry.Contains(GoodKey, StringComparison.Ordinal));
    }
}
