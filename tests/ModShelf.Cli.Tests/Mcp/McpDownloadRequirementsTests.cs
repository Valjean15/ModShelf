using System.Security.Cryptography;

using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

using static ModShelf.Cli.Tests.Fakes.FakeRequiringSource;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>download</c> with <c>withRequirements</c>: the mods each link's mod needs that the library lacks download with it,
/// each by its newest main file, and what's off the site is listed for the user (ADR 0102). No desktop app runs, so the
/// server runs the downloads itself.
/// </summary>
public sealed class McpDownloadRequirementsTests : IAsyncDisposable
{
    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public McpDownloadRequirementsTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeRequiringSource(_server.FileUrl)]);
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
    public async Task DownloadsTheModsTheLinksModNeedsWithItAndListsWhatsOffTheSite()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download", Arguments(("links", new[] { FileLink("main").AbsoluteUri }), ("withRequirements", true)), Cancellation);

        LinkRequirements required = Assert.Single(downloaded.Requirements!);
        Assert.Equal((FileLink("main").AbsoluteUri, true, null), (required.Link, required.Listed, required.Problem));
        Assert.Equal(
            [("Lib Mod", "download", 1, null), ("Script Extender", "elsewhere", 1, null), ("Bare Mod", "cantDownload", 1, null)],
            required.Requirements.Select(requirement => (requirement.Name, requirement.State, requirement.Depth, requirement.NeededBy)));
        RequirementEntry lib = required.Requirements[0];
        Assert.Equal((ModLink("lib").AbsoluteUri, FileNameOf("lib")), (lib.Page, lib.File));
        Assert.Null(lib.Download!.Problem);
        Assert.Equal(("lib", "1", FileNameOf("lib")), (lib.Download.Download!.ModId, lib.Download.Download.FileId, lib.Download.Download.FileName));
        RequirementEntry extender = required.Requirements[1];
        Assert.Equal((ExtenderPage, "Get the build for your game's version.", null), (extender.Page, extender.Notes, extender.Download));
        Assert.Equal(
            "It's off Fake requiring site, so ModShelf doesn't download it: the user gets it from its page.",
            extender.Message);
        Assert.Equal(
            "It can't be downloaded: Fake requiring site lists no main file of it to download.",
            required.Requirements[2].Message);
        Assert.Equal(
            "Fake requiring site lists 3 things mod main needs: 1 downloading with it, 1 off the site, for the user to get from "
            + "its page, and 1 with no file to download. The site's list can be wrong or out of date, so the mod's description "
            + "is worth a look too. Nothing is installed.",
            required.Message);
        Assert.EndsWith(
            " 1 mod they need downloads too; requirements says what each link's mod needs, and what's off the site for the user to get.",
            downloaded.Message,
            StringComparison.Ordinal);
        await WaitForModsAsync(mcp, "lib", "main");
    }

    [Fact]
    public async Task DeepAlsoDownloadsWhatThoseModsNeed()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download",
            Arguments(("links", new[] { ModLink("main").AbsoluteUri }), ("withRequirements", true), ("deep", true)),
            Cancellation);

        LinkRequirements required = Assert.Single(downloaded.Requirements!);
        Assert.Equal(
            [("Lib Mod", 1, null), ("Core Mod", 2, "Lib Mod"), ("Script Extender", 1, null), ("Bare Mod", 1, null)],
            required.Requirements.Select(requirement => (requirement.Name, requirement.Depth, requirement.NeededBy)));
        Assert.Equal("download", required.Requirements[1].State);
        await WaitForModsAsync(mcp, "core", "lib", "main");
    }

    [Fact]
    public async Task LeavesOutWhatTheLibraryHasAndWhatAnotherLinkGets()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<DownloadsResult>("download", Arguments(("links", new[] { FileLink("core").AbsoluteUri })), Cancellation);
        await WaitForModsAsync(mcp, "core");

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download",
            Arguments(("links", new[] { FileLink("main").AbsoluteUri, FileLink("lib").AbsoluteUri }), ("withRequirements", true), ("deep", true)),
            Cancellation);

        Assert.Equal(2, downloaded.Requirements!.Count);
        RequirementEntry lib = downloaded.Requirements[0].Requirements[0];
        Assert.Equal("Lib Mod", lib.Name);
        Assert.True(lib.State is "downloading" or "inLibrary", lib.State);
        Assert.Null(lib.Download);
        Assert.Equal(
            ("Core Mod", "inLibrary", "The library has it already, so it isn't downloaded."),
            Assert.Single(downloaded.Requirements[1].Requirements, requirement => requirement.Name == "Core Mod") is var core
                ? (core.Name, core.State, core.Message)
                : default);
        Assert.EndsWith(
            " None of the mods they need downloads; requirements says what each link's mod needs, and what's off the site for the user to get.",
            downloaded.Message,
            StringComparison.Ordinal);
        await WaitForModsAsync(mcp, "core", "lib", "main");
    }

    [Fact]
    public async Task ALinkThatCouldntBeDownloadedHasItsNeedsLeftUnread()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);
        string[] links = ["a"];

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download", Arguments(("links", links), ("withRequirements", true)), Cancellation);

        LinkRequirements required = Assert.Single(downloaded.Requirements!);
        Assert.Equal(
            ("a", false, "The link couldn't be downloaded, so what its mod needs wasn't read."),
            (required.Link, required.Listed, required.Problem));
        Assert.Empty(required.Requirements);
    }

    [Fact]
    public async Task WithoutWithRequirementsNothingElseDownloads()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download", Arguments(("links", new[] { FileLink("main").AbsoluteUri })), Cancellation);

        Assert.Null(downloaded.Requirements);
        await WaitForModsAsync(mcp, "main");
    }

    [Fact]
    public async Task RefusesDeepWithoutWithRequirements()
    {
        await using McpSession mcp = await _cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync(
            "download", Arguments(("links", new[] { FileLink("main").AbsoluteUri }), ("deep", true)), Cancellation);

        Assert.EndsWith("deep works with withRequirements only.", refused, StringComparison.Ordinal);
        Assert.Empty(_server.Ranges);
    }

    private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        => arguments.ToDictionary(argument => argument.Name, argument => argument.Value);

    /// <summary>
    /// Waits until the library has exactly these mods, each with its file, as their downloads end.
    /// </summary>
    private static async Task WaitForModsAsync(McpSession mcp, params string[] mods)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        patience.CancelAfter(Patience);
        string[] now = [];
        while (!now.SequenceEqual(mods))
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), patience.Token);
            }
            catch (OperationCanceledException) when (!Cancellation.IsCancellationRequested)
            {
                Assert.Fail($"The library has {string.Join(", ", now)}, not {string.Join(", ", mods)}.");
            }

            now = [.. (await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods
                .Where(mod => mod.Files is { Count: > 0 })
                .Select(mod => mod.ModId)
                .Order(StringComparer.Ordinal)];
        }
    }
}
