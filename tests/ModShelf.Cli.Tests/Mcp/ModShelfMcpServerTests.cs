using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModelContextProtocol.Client;

using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

using static ModShelf.Cli.Tests.Fakes.FakeHandoffSource;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// <c>mcp</c>: ModShelf's MCP server, with a client connected as an agent's app connects, in this process. Its tools
/// read the library, the updates, the queue and the settings, download into the library, and change the queue and the
/// settings. No desktop app runs, so the server runs the downloads itself, unless a test stands one in.
/// </summary>
public sealed class ModShelfMcpServerTests : IAsyncDisposable
{
    private const int Done = 0;

    private static readonly byte[] Content = RandomNumberGenerator.GetBytes(16 * 1024);
    private static readonly string Sha256OfContent = Convert.ToHexStringLower(SHA256.HashData(Content));
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly RangeFileServer _server = new(Content, "file.zip");
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly ScriptedQueueOwner _desktopApp = new();
    private readonly FakeHandoffSource _handoff;
    private readonly FakeCatalogSite _catalog = new();
    private InProcessCli? _cli;

    public ModShelfMcpServerTests()
    {
        _handoff = new FakeHandoffSource(_server.FileUrl);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private InProcessCli Cli => _cli ??= new InProcessCli(
        _site,
        _browser,
        sources: [_handoff, _catalog],
        configureServices: services =>
        {
            if (_desktopApp.Runs)
            {
                services.RemoveAll<IQueueOwner>();
                services.AddSingleton<IQueueOwner>(_desktopApp);
            }
        });

    private string ModFolder => Path.Combine(Cli.Downloads, "ModShelf", "fake-handoff", "game", $"{ModName} (mod)");

    public async ValueTask DisposeAsync()
    {
        if (_cli is not null)
        {
            await _cli.DisposeAsync();
        }

        _browser.Dispose();
        await _site.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ListsItsToolsAndSaysWhatModShelfIs()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: Cancellation);

        Assert.Equal(
            [
                "accounts_status", "diagnostics", "download", "downloads_cancel", "downloads_list", "downloads_pause",
                "downloads_resume", "downloads_retry", "library_changes", "library_check", "library_forget", "library_list",
                "library_note", "library_pin", "library_rescan", "library_skip", "library_unwatch", "library_updates", "library_watch",
                "mod_games", "mod_search", "mod_show", "settings_reset", "settings_set", "settings_show",
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ModShelfMcpServer.Instructions, mcp.Client.ServerInstructions);
        Assert.Equal("modshelf", mcp.Client.ServerInfo?.Name);
        Assert.All(
            tools.Where(tool => tool.Name.EndsWith("_list", StringComparison.Ordinal) || tool.Name.EndsWith("_show", StringComparison.Ordinal)),
            tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Equal(
            ["downloads_cancel", "settings_set"],
            tools.Where(tool => tool.ProtocolTool.Annotations?.DestructiveHint == true).Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.NotNull(tool.ProtocolTool.OutputSchema));
    }

    [Fact]
    public async Task EndsWhenItsClientClosesIt()
    {
        McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await using (mcp)
        {
            CliResult ended = await mcp.CloseAsync(Cancellation);

            Assert.Equal(Done, ended.ExitCode);
            Assert.Empty(ended.Output);
        }
    }

    [Fact]
    public async Task ShowsChangesAndResetsTheSettings()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        SettingResult changed = await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "library.oldVersions"), ("value", "archive")), Cancellation);
        SettingsListing shown = await mcp.CallAsync<SettingsListing>("settings_show", null, Cancellation);
        SettingResult reset = await mcp.CallAsync<SettingResult>("settings_reset", Arguments(("setting", "library.oldVersions")), Cancellation);

        Assert.Equal("library.oldVersions is now archive: when a newer version of a file is downloaded, the old ones move to an _archive folder.", changed.Message);
        Assert.Contains(shown.Settings, setting => setting is { Name: "library.oldVersions", Value: "archive", IsDefault: false });
        Assert.Contains(shown.Settings, setting => setting is { Name: "library.root", IsDefault: true } && setting.Value == Path.Combine(Cli.Downloads, "ModShelf"));
        Assert.Equal(Path.Combine(Cli.Folder, "settings.json"), shown.Location);
        Assert.StartsWith("library.oldVersions is back to its default, 2", reset.Message, StringComparison.Ordinal);
        Assert.Contains("archive", (await Cli.RunAsync(["settings", "show"], Cancellation)).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsChangesAndResetsAGamesOwnSettings()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        SettingResult changed = await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "games.nexus/stardewvalley.updateInterval"), ("value", "2d")), Cancellation);
        SettingsListing shown = await mcp.CallAsync<SettingsListing>("settings_show", null, Cancellation);
        SettingResult reset = await mcp.CallAsync<SettingResult>("settings_reset", Arguments(("setting", "games.nexus/stardewvalley.updateInterval")), Cancellation);

        Assert.Equal("games.nexus/stardewvalley.updateInterval is now 2d: the desktop app checks nexus/stardewvalley's watched mods for updates that often.", changed.Message);
        Assert.Contains(shown.Settings, setting => setting is { Name: "games.nexus/stardewvalley.updateInterval", Value: "2d", IsDefault: false });
        Assert.Contains("games.<site>/<game>.browse.pageSize", shown.Values, StringComparison.Ordinal);
        Assert.Equal("games.nexus/stardewvalley.updateInterval is gone, so nexus/stardewvalley goes by updates.interval again.", reset.Message);
    }

    [Fact]
    public async Task LeavesMovingAGamesFilesToTheUserAndFindsThemInItsFolder()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "2");
        string folder = Path.Combine(Cli.Folder, "Game mods");

        string refused = await mcp.CallFailingAsync("settings_set", Arguments(("setting", "games.fake-handoff/game.root"), ("value", folder)), Cancellation);
        CliResult moved = await Cli.RunAsync(["library", "move-game", "fake-handoff/game", folder, "--yes"], Cancellation);
        LibraryModEntry mod = Assert.Single((await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods);

        Assert.Contains(" Moving a game's files is for the user, with the CLI's library move-game.", refused, StringComparison.Ordinal);
        Assert.Equal(0, moved.ExitCode);
        Assert.StartsWith(folder + Path.DirectorySeparatorChar, Assert.Single(mod.Files!).Path, StringComparison.Ordinal);
        Assert.False(Assert.Single(mod.Files!).Missing);
    }

    [Fact]
    public async Task ChangesAGamesOwnPreviews()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        SettingResult changed = await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "games.nexus/stardewvalley.previews"), ("value", "cards")), Cancellation);
        SettingsListing shown = await mcp.CallAsync<SettingsListing>("settings_show", null, Cancellation);
        string refused = await mcp.CallFailingAsync("settings_set", Arguments(("setting", "games.nexus/stardewvalley.previews"), ("value", "grid")), Cancellation);

        Assert.StartsWith("games.nexus/stardewvalley.previews is now cards: ", changed.Message, StringComparison.Ordinal);
        Assert.Contains(shown.Settings, setting => setting is { Name: "games.nexus/stardewvalley.previews", Value: "cards", IsDefault: false });
        Assert.Contains("games.<site>/<game>.previews", shown.Values, StringComparison.Ordinal);
        Assert.Contains("'grid' isn't off, thumbnails or cards.", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGamesKeyAloneThatMoreThanOneSiteMayHaveIsRefusedNamingThem()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("settings_set", Arguments(("setting", "games.stardewvalley.notify"), ("value", "false")), Cancellation);

        Assert.Contains(
            "More than one site may have a game called stardewvalley: name its site too, as nexus/stardewvalley or fake-handoff/stardewvalley or fake-catalog/stardewvalley.",
            refused,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library.everything", "1", "There's no setting called library.everything.")]
    [InlineData("updates.interval", "soon", "soon")]
    [InlineData("appearance.theme", "pink", "'pink' isn't system, light or dark.")]
    public async Task ASettingThatIsntOneOrAValueThatIsntItsIsRefused(string setting, string value, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("settings_set", Arguments(("setting", setting), ("value", value)), Cancellation);

        Assert.Contains(problem, refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLibrarysRootDoesntMoveWithItsFiles()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "2");

        string refused = await mcp.CallFailingAsync(
            "settings_set",
            Arguments(("setting", "library.root"), ("value", Path.Combine(Cli.Folder, "Elsewhere"))),
            Cancellation);

        Assert.EndsWith("Moving the library's files is for the user, in the desktop app or with the CLI's library move.", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadsIntoTheLibraryItselfWhileNoDesktopAppRunsThenLetsTheQueueGo()
    {
        // Held part-way, so the result finds it under way.
        _server.HoldAt(1024);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult result = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);
        _server.SendEverything();
        _server.DropHeldConnections();

        Assert.Equal("this MCP server", result.RunBy);
        Assert.Equal(Path.Combine(ModFolder, FileNameOf("3")), result.LibraryPath);
        Assert.Contains("No desktop app is running, so this MCP server downloads it", result.Message, StringComparison.Ordinal);
        Assert.Equal(FileNameOf("3"), result.Download?.FileName);
        await WaitForFileAsync(mcp, "3");
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder, FileNameOf("3")), Cancellation));

        // Nothing is under way, so the server lets the queue go, and the CLI can run it.
        await WaitUntilAsync(
            async () => (await Cli.RunAsync(["download", HandoffLink("1").AbsoluteUri], Cancellation)).ExitCode == Done,
            "The MCP server kept the queue.");
    }

    [Fact]
    public async Task SeveralLinksDownloadInTheOrderGivenEachWithItsResult()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "2");

        // File 3 is held part-way, so it's still under way when the call ends.
        _server.HoldAt(1024);
        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download",
            Arguments(("links", new[] { HandoffLink("3").AbsoluteUri, "mods/2400", FileLink("1").AbsoluteUri, HandoffLink("2").AbsoluteUri })),
            Cancellation);
        _server.SendEverything();
        _server.DropHeldConnections();

        Assert.Equal(
            [
                (FileNameOf("3"), (string?)null, false),
                (null, "That isn't a complete link, such as https://www.nexusmods.com/stardewvalley/mods/2400.", false),
                (FileNameOf("1"), null, false),
                (null, null, true),
            ],
            downloaded.Results.Select(result => (result.Download?.FileName, result.Problem, result.AlreadySaved)));
        Assert.Equal(nameof(DownloadJobStatus.AwaitingHandoff), downloaded.Results[2].Download?.Status);
        Assert.Equal(
            "Of 4 links, 2 are downloading or waiting for the user, 1 was in the library already, and 1 couldn't be downloaded; each result says more, in the order the links were given.",
            downloaded.Message);
        await WaitForFileAsync(mcp, "3");
    }

    [Fact]
    public async Task TwoLinksThatCouldntBeDownloadedSaySo()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string[] links = ["a", "b"];

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>("download", Arguments(("links", links)), Cancellation);

        Assert.Equal("Of 2 links, 2 couldn't be downloaded; each result says more, in the order the links were given.", downloaded.Message);
    }

    [Fact]
    public async Task ArgumentsThatDontFitAToolSayWhatItTakes()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        // As an agent's app that read the tools from another ModShelf, calling library_changes with from.
        string refused = await mcp.CallFailingAsync("library_changes", Arguments(("from", "2026-11-01T09:00:00Z")), Cancellation);

        Assert.EndsWith(
            "library_changes doesn't take these arguments: it needs since. "
            + "It takes since (required). It was given from, which it doesn't take. The agent's list of ModShelf's tools may be "
            + "older than this MCP server: restarting the agent's app, then starting a new conversation, reads it again.",
            refused,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("download", "links", "\"https://www.nexusmods.com/stardewvalley/mods/2400\"", "links is a list of strings or null, not a string. It takes links, link.")]
    [InlineData("download", "links", "[\"https://www.nexusmods.com/stardewvalley/mods/2400\", 3]", "links is a list of strings or null, not a list. It takes links, link.")]
    [InlineData("download", "link", "[\"https://www.nexusmods.com/stardewvalley/mods/2400\"]", "link is a string or null, not a list. It takes links, link.")]
    [InlineData("library_list", "limit", "\"many\"", "limit is an integer, not a string. It takes game, source, mod, watchedOnly, updatesOnly, missingOnly, compact, offset, limit.")]
    [InlineData("library_list", "limit", "2.5", "limit is an integer, not a number. It takes game, source, mod, watchedOnly, updatesOnly, missingOnly, compact, offset, limit.")]
    [InlineData("library_list", "compact", "\"yes\"", "compact is true or false, not a string. It takes game, source, mod, watchedOnly, updatesOnly, missingOnly, compact, offset, limit.")]
    [InlineData("library_changes", "since", "null", "since is a string, not null. It takes since (required).")]
    public async Task AnArgumentOfAnotherTypeSaysWhatTheToolTakes(string tool, string name, string json, string said)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync(tool, new Dictionary<string, object?> { [name] = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone() }, Cancellation);

        Assert.Contains($"{tool} doesn't take these arguments: {said} The agent's list of ModShelf's tools", refused, StringComparison.Ordinal);
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task ArgumentsThatFitStillReachTheTool()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        // A nullable argument given as null, and an argument the tool doesn't take, which is left aside.
        LibraryListing listed = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("game", null), ("limit", 5), ("extra", true)), Cancellation);

        Assert.Equal((0, false), (listed.Total, listed.HasMore));
    }

    [Theory]
    [InlineData(0, "'links' needs at least one link.")]
    [InlineData(51, "Give from 1 to 50 links.")]
    public async Task NoLinksOrTooManyAreRefused(int count, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("download", Arguments(("links", Enumerable.Repeat(HandoffLink("3").AbsoluteUri, count).ToArray())), Cancellation);

        Assert.EndsWith(problem, refused, StringComparison.Ordinal);
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task ACallWithNoLinkAtAllIsRefused()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("download", null, Cancellation);

        Assert.EndsWith("'links' needs at least one link.", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOldLinkStillDownloadsAndSaysItsDeprecated()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "3");
        string link = HandoffLink("3").AbsoluteUri;

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>("download", Arguments(("link", link)), Cancellation);

        Assert.True(Assert.Single(downloaded.Results).AlreadySaved);
        DeprecatedArgument deprecated = Assert.IsType<DeprecatedArgument>(downloaded.Deprecated);
        Assert.Equal(("DL_LINK_ARG", "link", "links"), (deprecated.Code, deprecated.Argument, deprecated.Use));
        Assert.Equal([link], deprecated.Example.Links);
        Assert.Equal(
            "Note: 'link' is the old way to call download and will stop working; pass 'links' as a list instead, as in the example. "
            + "The library has this file already, so nothing downloads.",
            downloaded.Message);
        string warning = Assert.Single(Cli.Logs.Entries, entry => entry.Contains("DL_LINK_ARG", StringComparison.Ordinal));
        Assert.StartsWith(
            $"[ModShelf.Cli.Mcp.DownloadTools] DL_LINK_ARG download called with legacy 'link'; normalized to links=[1 link] (client: {McpSession.ClientName}) |",
            warning,
            StringComparison.Ordinal);
        Assert.DoesNotContain(link, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOldLinkAndLinksTogetherAreRefused()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync(
            "download", Arguments(("link", HandoffLink("3").AbsoluteUri), ("links", new[] { HandoffLink("3").AbsoluteUri })), Cancellation);

        Assert.EndsWith("Pass only 'links'; 'link' is the old name for a single link.", refused, StringComparison.Ordinal);
        Assert.Empty(_server.Ranges);
    }

    [Fact]
    public async Task DownloadTakesLinksOrTheDeprecatedLinkAndNothingElse()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        McpClientTool download = Assert.Single(await mcp.Client.ListToolsAsync(cancellationToken: Cancellation), tool => tool.Name == "download");

        System.Text.Json.JsonElement schema = download.ProtocolTool.InputSchema;
        Assert.Equal(["link", "links"], schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(schema.TryGetProperty("required", out System.Text.Json.JsonElement required) && required.GetArrayLength() > 0);
        Assert.EndsWith("(link, a single link, still works but is deprecated.)", schema.GetProperty("properties").GetProperty("links").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileTheLibraryHasIsntDownloadedAgain()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "3");

        LinkDownloadResult again = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);

        Assert.True(again.AlreadySaved);
        Assert.Null(again.Download);
        Assert.Equal("The library has this file already, so nothing downloads.", again.Message);
        Assert.Single(_server.Ranges);
    }

    [Fact]
    public async Task ADownloadTheSiteWantsTheUserToStartWaitsWithItsPageAndLetsTheQueueGo()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult result = await DownloadAsync(mcp, FileLink("1").AbsoluteUri);

        Assert.Equal((nameof(DownloadJobStatus.AwaitingHandoff), Page("1").AbsoluteUri), (result.Download?.Status, result.Download?.HandoffPage));
        Assert.StartsWith("The site needs the user to start this download on its website", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("this MCP server downloads it", result.Message, StringComparison.Ordinal);

        // Waiting for the user isn't under way: the desktop app, which the site's link starts, takes the queue over.
        await WaitUntilAsync(
            async () => (await Cli.RunAsync(["download", HandoffLink("2").AbsoluteUri], Cancellation)).ExitCode == Done,
            "The MCP server kept the queue.");
    }

    [Fact]
    public async Task WhileItRunsTheDownloadsItListsThePagesOfTheOnesWaitingForTheUser()
    {
        // The pages are in the memory of the process that runs the queue, not in the database. A download held part-way
        // keeps the queue running here.
        _server.HoldAt(1024);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);
        await DownloadAsync(mcp, FileLink("1").AbsoluteUri);

        DownloadListing listed = await mcp.CallAsync<DownloadListing>("downloads_list", null, Cancellation);

        Assert.Equal(
            [(FileNameOf("3"), nameof(DownloadJobStatus.Downloading), null), (FileNameOf("1"), nameof(DownloadJobStatus.AwaitingHandoff), Page("1").AbsoluteUri)],
            listed.Downloads.Select(download => (download.FileName, download.Status, download.HandoffPage)));
        _server.SendEverything();
        _server.DropHeldConnections();
        await WaitForFileAsync(mcp, "3");
    }

    [Fact]
    public async Task ChangesADownloadByItsIdOrItsStart()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAsync(mcp, FileLink("1").AbsoluteUri);
        DownloadEntry waiting = await WaitForDownloadAsync(mcp, download => download.Status == nameof(DownloadJobStatus.AwaitingHandoff));

        DownloadResult cancelled = await mcp.CallAsync<DownloadResult>("downloads_cancel", Arguments(("id", waiting.Id[..8])), Cancellation);

        Assert.Equal(nameof(DownloadJobStatus.Cancelled), cancelled.Download?.Status);
        Assert.Equal(waiting.Id, cancelled.Download?.Id);
        Assert.Equal("Cancelled.", cancelled.Message);
        Assert.Contains("isn't allowed", await mcp.CallFailingAsync("downloads_pause", Arguments(("id", waiting.Id)), Cancellation), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1234", "its first 8 characters or more")]
    [InlineData("ffffffff", "No download has an ID that starts with ffffffff.")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", "No download has the ID ffffffff-ffff-ffff-ffff-ffffffffffff.")]
    public async Task ADownloadThatIsntThereIsRefused(string id, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("downloads_resume", Arguments(("id", id)), Cancellation);

        Assert.Contains(problem, refused, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mods/2400", "That isn't a complete link")]
    [InlineData("https://handoff.example/missing/1", "The site has no such mod or file.")]
    [InlineData("https://handoff.example/locked/1", "The site needs a linked account for that")]
    [InlineData("https://elsewhere.example/mods/1", "")]
    public async Task ALinkItCantDownloadIsRefused(string link, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult refused = await DownloadAsync(mcp, link);

        Assert.Contains(problem, refused.Problem, StringComparison.Ordinal);
        Assert.Equal(refused.Problem, refused.Message);
        Assert.DoesNotContain("Something went wrong", refused.Problem, StringComparison.Ordinal);
        Assert.Equal((null, null, null), (refused.Download, refused.LibraryPath, refused.RunBy));
    }

    [Fact]
    public async Task ADesktopAppThatRunsDownloadsInstead()
    {
        _desktopApp.Runs = true;
        DownloadJobSnapshot job = new(
            DownloadJobId.New(),
            new ModFileRef(new ModRef(new GameRef(FakeHandoffSource.SourceId, "game"), "mod"), "3"),
            ModFolder,
            FileNameOf("3"),
            DownloadJobStatus.Queued,
            0,
            null,
            null,
            DateTimeOffset.UnixEpoch);
        _desktopApp.Reply = new QueueReply(QueueOutcome.Done, Job: job, LibraryPath: Path.Combine(ModFolder, FileNameOf("3")));
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult result = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);

        Assert.Equal("desktop app", result.RunBy);
        Assert.Equal(job.Id.ToString(), result.Download?.Id);
        Assert.DoesNotContain("this MCP server", result.Message, StringComparison.Ordinal);
        Assert.Equal(QueueRequest.Download(HandoffLink("3")), Assert.Single(_desktopApp.Requests));
        Assert.Empty(_server.Ranges);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Completed, null, "The file is downloaded into the library. library_changes says what became of the version it replaced, as library.oldVersions says.")]
    [InlineData(DownloadJobStatus.Failed, DownloadFailureReason.Network, "The download failed (Network). downloads_retry tries it again.")]
    [InlineData(DownloadJobStatus.Paused, null, "The download is paused. downloads_resume goes on with it.")]
    [InlineData(DownloadJobStatus.Downloading, null, "The file is downloading into the library. downloads_list shows how it goes, and the page to open when the site needs the user to start it there.")]
    public async Task SaysWhatADownloadIsWhenTheCallEndsNotWhenItWasAskedFor(DownloadJobStatus status, DownloadFailureReason? failure, string message)
    {
        // The desktop app took it as queued; by the time the call ends, the queue has it further along.
        await DesktopAppHasAsync(status, failure);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult result = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);

        Assert.Equal((status.ToString(), failure?.ToString(), message), (result.Download?.Status, result.Download?.Failure, result.Message));
    }

    [Fact]
    public async Task AFileDownloadedOutsideTheLibraryIsSaidToBe()
    {
        await DesktopAppHasAsync(DownloadJobStatus.Completed, null, inLibrary: false);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult result = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);

        Assert.Equal("The file is downloaded, outside the library, at the download's path.", result.Message);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Completed, "2 are downloaded")]
    [InlineData(DownloadJobStatus.Failed, "2 failed")]
    public async Task SeveralLinksSayHowManyAreDownloadedOrFailedWhenTheCallEnds(DownloadJobStatus status, string said)
    {
        await DesktopAppHasAsync(status, status == DownloadJobStatus.Failed ? DownloadFailureReason.Network : null);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>(
            "download", Arguments(("links", new[] { HandoffLink("3").AbsoluteUri, HandoffLink("3").AbsoluteUri, "a" })), Cancellation);

        Assert.Equal(
            $"Of 3 links, {said}, and 1 couldn't be downloaded; each result says more, in the order the links were given.",
            downloaded.Message);
    }

    [Theory]
    [InlineData(QueueOutcome.NotAllowed, "A finished download can't be paused.")]
    [InlineData(QueueOutcome.Refused, "The site refused.")]
    public async Task WhatTheDesktopAppRefusesIsTheToolsError(QueueOutcome outcome, string problem)
    {
        _desktopApp.Runs = true;
        _desktopApp.Reply = new QueueReply(outcome, problem);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync("downloads_pause", Arguments(("id", Guid.NewGuid().ToString())), Cancellation);

        Assert.EndsWith(problem, refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADesktopAppThatDoesntReplySaysItMayHaveDoneIt()
    {
        _desktopApp.Runs = true;
        _desktopApp.Fails = new TimeoutException("No reply.");
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        LinkDownloadResult refused = await DownloadAsync(mcp, HandoffLink("3").AbsoluteUri);

        Assert.Contains("didn't reply in time; it may have carried it out. downloads_list shows the queue.", refused.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhatCameIntoTheLibraryWithTheVersionsItHadBefore()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "library.oldVersions"), ("value", "archive")), Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "2");
        LibraryChanges first = await ChangesAsync(mcp, start);

        await DownloadAndWaitAsync(mcp, "3");
        LibraryChanges next = await ChangesAsync(mcp, first.Until);

        NewLibraryFile two = Assert.Single(first.Files);
        Assert.Equal(("fake-handoff", "game", "mod", ModName, "2", "1.2"), (two.Source, two.Game, two.ModId, two.ModName, two.File.FileId, two.File.Version));
        Assert.Empty(two.OtherFiles);
        NewLibraryFile three = Assert.Single(next.Files);
        Assert.Equal(("3", Path.Combine(ModFolder, FileNameOf("3")), false), (three.File.FileId, three.File.Path, three.File.Archived));
        LibraryFileEntry before = Assert.Single(three.OtherFiles);
        Assert.Equal(("2", "1.2", true), (before.FileId, before.Version, before.Archived));
        Assert.Equal(Path.Combine(ModFolder, "_archive", FileNameOf("2")), before.Path);

        // Read once, as each came in: archiving keeps it.
        Assert.Equal((Sha256OfContent, Content.Length), (three.File.Sha256, (int)three.File.SizeBytes));
        Assert.Equal(Sha256OfContent, before.Sha256);
        Assert.True(File.Exists(before.Path));
        Assert.Equal("archive", next.OldVersions);
        Assert.Equal(2, (await ChangesAsync(mcp, start)).Files.Count);
    }

    [Fact]
    public async Task SaysWhichVersionEachFileReplacedAndWhatBecameOfIt()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAndWaitAsync(mcp, "2");
        await DownloadAndWaitAsync(mcp, "3");

        LibraryChanges changes = await ChangesAsync(mcp, start);

        // By default the newest two versions stay: 3 made 1 the third, deleted.
        Assert.Equal(
            [("2", new ReplacedFileEntry("1", "1.1", "File 1", "deleted")), ("3", new ReplacedFileEntry("2", "1.2", "File 2", "kept"))],
            changes.Files.Select(file => (file.File.FileId, file.Replaces)));
    }

    [Fact]
    public async Task SaysAReplacedVersionArchivedOrGoneFromTheLibrary()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "library.oldVersions"), ("value", "archive")), Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAndWaitAsync(mcp, "2");
        await DownloadAndWaitAsync(mcp, "3");
        ModRef mod = new(new GameRef(FakeHandoffSource.SourceId, "game"), "mod");
        await Cli.InAppAsync(
            services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(mod, stored => stored!.WithoutFile(new ModFileRef(mod, "1")), Cancellation),
            Cancellation);

        LibraryChanges changes = await ChangesAsync(mcp, start);

        Assert.Equal(
            [("2", new ReplacedFileEntry("1", "1.1", "File 1", "gone")), ("3", new ReplacedFileEntry("2", "1.2", "File 2", "archived"))],
            changes.Files.Select(file => (file.File.FileId, file.Replaces)));
    }

    [Fact]
    public async Task SaysAVersionTheNewestKeptLeftOutDeletedAsItCame()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "library.oldVersions"), ("value", "1")), Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAndWaitAsync(mcp, "2");

        LibraryChanges changes = await ChangesAsync(mcp, start);

        NewLibraryFile two = Assert.Single(changes.Files);
        Assert.Equal(new ReplacedFileEntry("1", "1.1", "File 1", "deleted"), two.Replaces);
    }

    [Fact]
    public async Task AFileForgottenAndFoundAgainSaysNothingOfItsDownload()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAndWaitAsync(mcp, "2");
        await mcp.CallAsync<ForgetResult>("library_forget", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        await mcp.CallAsync<RescanResult>("library_rescan", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);

        LibraryChanges changes = await ChangesAsync(mcp, start);

        // The library has the files as the rescan found them, not as they were downloaded.
        Assert.Equal([("1", (ReplacedFileEntry?)null), ("2", null)], changes.Files.Select(file => (file.File.FileId, file.Replaces)));
    }

    [Fact]
    public async Task AFileFoundByARescanReplacesNothingItSays()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await File.WriteAllBytesAsync(Path.Combine(ModFolder, FileNameOf("2")), Content, Cancellation);
        await mcp.CallAsync<RescanResult>("library_rescan", null, Cancellation);

        LibraryChanges changes = await ChangesAsync(mcp, start);

        Assert.Equal([("1", (ReplacedFileEntry?)null), ("2", null)], changes.Files.Select(file => (file.File.FileId, file.Replaces)));
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("")]
    public async Task ATimeThatIsntOneIsRefused(string since)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        Assert.Contains(
            "since isn't a date and time, such as 2026-11-01T09:00:00Z.",
            await mcp.CallFailingAsync("library_changes", Arguments(("since", since)), Cancellation),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RescansTheLibraryAndBringsAForgottenModBackByItsLink()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        string placed = Path.Combine(ModFolder, FileNameOf("2"));
        await File.WriteAllBytesAsync(placed, Content, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(ModFolder, "notes.txt"), "mine", Cancellation);

        RescanResult whole = await mcp.CallAsync<RescanResult>("library_rescan", null, Cancellation);
        await mcp.CallAsync<ForgetResult>("library_forget", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        RescanResult back = await mcp.CallAsync<RescanResult>("library_rescan", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);

        Assert.Equal("Rescanned 1 mod: 1 files added, 0 hashed, 0 missing from disk, 1 matching nothing.", whole.Message);
        RescannedFile added = Assert.Single(whole.Added);
        Assert.Equal(("fake-handoff", "game", "mod", "2"), (added.Source, added.Game, added.ModId, added.File.FileId));
        Assert.Equal((placed, Sha256OfContent), (added.File.Path, added.File.Sha256));
        Assert.Equal([Path.Combine(ModFolder, "notes.txt")], whole.Unmatched);
        Assert.Equal(["1", "2"], back.Added.Select(file => file.File.FileId).Order());
        LibraryModEntry mod = Assert.Single((await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods);
        Assert.Equal((true, 2), (mod.Watched, mod.FileCount));
    }

    [Fact]
    public async Task ANewOptionalFileIsANewFileThatReplacesNone()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        ModRef mod = new(new GameRef(FakeHandoffSource.SourceId, "game"), "mod");
        await Cli.InAppAsync(
            services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                mod,
                stored => stored!.WithUpdates(
                [
                    new LibraryUpdate(
                        new ModFileRef(mod, "7"), "Extra textures", "extra.zip", "1.0", ModFileCategory.Optional, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, FileLink("7")),
                ]),
                Cancellation),
            Cancellation);

        UpdateListing updates = await mcp.CallAsync<UpdateListing>("library_updates", null, Cancellation);
        LibraryListing library = await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation);

        LibraryUpdateEntry found = Assert.Single(updates.Updates).Update;
        Assert.Equal(("7", "Optional", "newFile", (LibraryFileEntry?)null), (found.FileId, found.Category, found.Kind, found.Replaces));
        Assert.Equal("newFile", Assert.Single(Assert.Single(library.Mods).Updates!).Kind);
    }

    [Fact]
    public async Task PinsAModWhoseUpdatesAreThenLeftOut()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        await StoreUpdatesAsync("2", "7");

        PinResult pinned = await mcp.CallAsync<PinResult>("library_pin", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        PinResult again = await mcp.CallAsync<PinResult>("library_pin", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        UpdateListing updates = await mcp.CallAsync<UpdateListing>("library_updates", null, Cancellation);
        LibraryListing listed = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("updatesOnly", true)), Cancellation);
        LibraryModEntry mod = Assert.Single((await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods);
        PinResult unpinned = await mcp.CallAsync<PinResult>("library_pin", Arguments(("link", FileLink("1").AbsoluteUri), ("pinned", false)), Cancellation);

        Assert.Equal((true, $"Pinned {ModName}: its updates are still found, but aren't offered."), (pinned.Pinned, pinned.Message));
        Assert.Equal($"{ModName} is pinned already.", again.Message);
        Assert.Empty(updates.Updates);
        Assert.Empty(listed.Mods);
        Assert.Equal((true, 0, 2), (mod.Pinned, mod.UpdateCount, mod.Updates!.Count));
        Assert.Equal((false, $"Unpinned {ModName}: its updates are offered again.", 2), (unpinned.Pinned, unpinned.Message, unpinned.Mod.UpdateCount));
        Assert.Equal(2, (await mcp.CallAsync<UpdateListing>("library_updates", null, Cancellation)).Updates.Count);
    }

    [Fact]
    public async Task SkipsAnUpdateByItsLinkUntilANewerOneComes()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        await StoreUpdatesAsync("2", "7");

        SkipResult skipped = await mcp.CallAsync<SkipResult>("library_skip", Arguments(("link", FileLink("2").AbsoluteUri)), Cancellation);
        SkipResult again = await mcp.CallAsync<SkipResult>("library_skip", Arguments(("link", FileLink("2").AbsoluteUri)), Cancellation);
        UpdateListing updates = await mcp.CallAsync<UpdateListing>("library_updates", null, Cancellation);
        LibraryModEntry mod = Assert.Single((await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods);
        ModDetails shown = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FileLink("2").AbsoluteUri)), Cancellation);
        SkipResult offered = await mcp.CallAsync<SkipResult>("library_skip", Arguments(("link", FileLink("2").AbsoluteUri), ("skip", false)), Cancellation);

        Assert.Equal((true, $"Skipped Update 2 of {ModName} until a newer update comes.", "2", true), (skipped.Skip, skipped.Message, skipped.Update?.FileId, skipped.Update?.Skipped));
        Assert.Equal(($"That update of {ModName} is skipped already.", (LibraryUpdateEntry?)null), (again.Message, again.Update));
        Assert.Equal(["7"], updates.Updates.Select(found => found.Update.FileId));
        Assert.Equal(1, mod.UpdateCount);
        Assert.Equal([("2", true), ("7", false)], mod.Updates!.Select(update => (update.FileId, update.Skipped)).Order());
        Assert.Equal([("3", false), ("2", true), ("1", false)], shown.Files.Select(file => (file.FileId, file.Skipped)));
        Assert.Equal(($"Update 2 of {ModName} is offered again.", false), (offered.Message, offered.Update!.Skipped));
    }

    [Fact]
    public async Task SkippingAFileThatIsntAnUpdateIsRefused()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");

        Assert.Contains(
            "File 1 isn't an update of the mod.",
            await mcp.CallFailingAsync("library_skip", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsANoteThatTheListAndTheModShow()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");

        NoteResult noted = await mcp.CallAsync<NoteResult>("library_note", Arguments(("link", FileLink("1").AbsoluteUri), ("note", "Stay on 1.1.")), Cancellation);
        LibraryModEntry listed = Assert.Single((await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods);
        ModDetails shown = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        NoteResult removed = await mcp.CallAsync<NoteResult>("library_note", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);

        Assert.Equal(("Stay on 1.1.", $"Kept the note of {ModName}."), (noted.Note, noted.Message));
        Assert.Equal(("Stay on 1.1.", false), (listed.Note, listed.Pinned));
        Assert.Equal(("Stay on 1.1.", false), (shown.Note, shown.Pinned));
        Assert.Equal(((string?)null, $"Removed the note of {ModName}.", (string?)null), (removed.Note, removed.Message, removed.Mod.Note));
        Assert.Contains(
            "A note holds at most 2,000 characters",
            await mcp.CallFailingAsync("library_note", Arguments(("link", FileLink("1").AbsoluteUri), ("note", new string('a', 2001))), Cancellation),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library_pin")]
    [InlineData("library_note")]
    public async Task PinningOrNotingAModNotInTheLibraryIsRefused(string tool)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        Assert.Contains(
            "Mod mod of game on fake-handoff isn't in the library.",
            await mcp.CallFailingAsync(tool, Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChecksTheWatchedModsAndListsTheUpdatesWithWhatTheyReplace()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        WatchResult watched = await mcp.CallAsync<WatchResult>("library_watch", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);

        UpdateCheckResult check = await mcp.CallAsync<UpdateCheckResult>("library_check", null, Cancellation);
        UpdateListing updates = await mcp.CallAsync<UpdateListing>("library_updates", Arguments(("changelog", true)), Cancellation);

        Assert.True(watched.Watched);
        Assert.Equal($"Watching {ModName} (mod mod of game on fake-handoff).", watched.Message);
        Assert.Equal(1, check.Checked);
        Assert.Empty(check.Failures);
        UpdateFound newest = Assert.Single(check.Found);
        Assert.Equal(("3", "1.3", FileLink("3").AbsoluteUri, "1"), (newest.Update.FileId, newest.Update.Version, newest.Update.Link, newest.Update.Replaces?.FileId));
        Assert.Equal(Path.Combine(ModFolder, FileNameOf("1")), newest.Update.Replaces?.Path);
        Assert.Equal("update", newest.Update.Kind);
        UpdateFound listed = Assert.Single(updates.Updates);
        Assert.Equal(["Faster.", "Fixed the crash."], listed.Changes);
        Assert.NotNull(updates.CheckedAt);
        Assert.Null((await mcp.CallAsync<UpdateListing>("library_updates", null, Cancellation)).Updates[0].Changes);
    }

    [Fact]
    public async Task ListsTheLibraryAndUnwatchesAMod()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<WatchResult>("library_watch", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);
        await DownloadAndWaitAsync(mcp, "1");

        LibraryListing library = await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation);
        WatchResult unwatched = await mcp.CallAsync<WatchResult>("library_unwatch", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);
        WatchResult again = await mcp.CallAsync<WatchResult>("library_unwatch", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);

        Assert.Equal(Path.Combine(Cli.Downloads, "ModShelf"), library.Root);
        Assert.Equal([("fake-catalog", "1", true, 0), ("fake-handoff", "mod", true, 1)], library.Mods.Select(mod => (mod.Source, mod.ModId, mod.Watched, mod.FileCount)));
        Assert.Equal((Path.Combine(ModFolder, FileNameOf("1")), false), (library.Mods[1].Files?[0].Path, library.Mods[1].Files?[0].Missing));
        Assert.Equal("Stopped watching Example Mod. It had no downloaded files, so it's out of the library.", unwatched.Message);
        Assert.Null(unwatched.Mod);
        Assert.Equal("Mod 1 of game on fake-catalog isn't in the library.", again.Message);
    }

    [Fact]
    public async Task ForgetsAModWhoseFilesStayOnDisk()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        await mcp.CallAsync<WatchResult>("library_watch", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);

        ForgetResult forgot = await mcp.CallAsync<ForgetResult>("library_forget", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        ForgetResult again = await mcp.CallAsync<ForgetResult>("library_forget", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        ForgetResult withoutFiles = await mcp.CallAsync<ForgetResult>("library_forget", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);
        LibraryListing library = await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation);

        Assert.True(forgot.Forgotten);
        Assert.Equal(
            $"{ModName} is out of the library: ModShelf doesn't watch it or keep track of its files any more. Its downloaded file stays on disk, at the path in mod.files.",
            forgot.Message);
        Assert.Equal((Path.Combine(ModFolder, FileNameOf("1")), false), (forgot.Mod?.Files?[0].Path, forgot.Mod?.Files?[0].Missing));
        Assert.Equal(Content, await File.ReadAllBytesAsync(Path.Combine(ModFolder, FileNameOf("1")), Cancellation));
        Assert.Equal((false, "Mod mod of game on fake-handoff isn't in the library.", (LibraryModEntry?)null), (again.Forgotten, again.Message, again.Mod));
        Assert.EndsWith("It had no downloaded files.", withoutFiles.Message, StringComparison.Ordinal);
        Assert.Equal(0, library.Total);

        // Its completed download left the queue as saved, so no later start records it again.
        IReadOnlyList<DownloadJob> saved = await Cli.InAppAsync(services => services.GetRequiredService<IDownloadJobRepository>().ListAsync(Cancellation), Cancellation);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task AModWithADownloadIntoTheLibraryUnderWayIsntForgotten()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAsync(mcp, FileLink("3").AbsoluteUri);

        string refused = await mcp.CallFailingAsync("library_forget", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);

        Assert.EndsWith($": A download of a file of {ModName} into the library isn't finished. Let it finish, or cancel it, first.", refused, StringComparison.Ordinal);
        Assert.Equal(1, (await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Total);
    }

    [Fact]
    public async Task ShowsAModWithItsFilesAndWhereTheLibraryHasThem()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "2");

        ModDetails mod = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FileLink("2").AbsoluteUri), ("changelog", true)), Cancellation);
        ModDetails catalog = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);
        ModDetails all = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri), ("allFiles", true)), Cancellation);

        Assert.Equal((ModName, "Handoff Game", true), (mod.Name, mod.GameName, mod.Watched));
        Assert.Equal(["3", "2", "1"], mod.Files.Select(file => file.FileId));
        ModFileEntry two = mod.Files[1];
        Assert.Equal((true, Path.Combine(ModFolder, FileNameOf("2")), "3", true, FileLink("2").AbsoluteUri), (two.Linked, two.LibraryPath, two.ReplacedBy, two.ReplacedByOnSite, two.Link));
        Assert.Equal((null, null, false), (mod.Files[0].LibraryPath, mod.Files[0].ReplacedByOnSite, mod.Files[0].Missing));
        Assert.Equal((Sha256OfContent, (string?)null), (two.LibrarySha256, mod.Files[0].LibrarySha256));
        Assert.Empty(mod.LibraryOnly);
        Assert.False(mod.NewerInLibrary);
        Assert.Equal(["1.2", "1.3"], mod.Changelog?.Select(entry => entry.Version));
        Assert.Equal(["12", "11", "20"], catalog.Files.Select(file => file.FileId));
        Assert.Equal(3, catalog.HiddenFiles);
        Assert.Null(catalog.Changelog);
        Assert.True(catalog.Adult);
        Assert.Equal(6, all.Files.Count);
        Assert.Equal(0, all.HiddenFiles);
    }

    [Fact]
    public async Task AFilePulledFromTheSiteThatTheLibraryHasIsShownWithItsPlace()
    {
        // As Dynamic Menu Overhaul's 2.8: the site lists 2.7, replaced by a file it no longer lists, which you have.
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await DownloadAndWaitAsync(mcp, "3");
        _handoff.Pulled.Add("3");

        ModDetails mod = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FileLink("2").AbsoluteUri)), Cancellation);

        Assert.Equal(["2", "1"], mod.Files.Select(file => file.FileId));
        Assert.Equal(("3", false), (mod.Files[0].ReplacedBy, mod.Files[0].ReplacedByOnSite));
        LibraryFileEntry pulled = Assert.Single(mod.LibraryOnly);
        Assert.Equal(("3", "1.3", Path.Combine(ModFolder, FileNameOf("3"))), (pulled.FileId, pulled.Version, pulled.Path));
        Assert.True(mod.NewerInLibrary);
    }

    [Fact]
    public async Task AFileGoneFromDiskIsMissing()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await mcp.CallAsync<SettingResult>("settings_set", Arguments(("setting", "library.oldVersions"), ("value", "all")), Cancellation);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DownloadAndWaitAsync(mcp, "1");
        await DownloadAndWaitAsync(mcp, "2");
        await DownloadAndWaitAsync(mcp, "3");
        File.Delete(Path.Combine(ModFolder, FileNameOf("3")));
        await mcp.CallAsync<WatchResult>("library_watch", Arguments(("link", FakeCatalogSite.ModLink("1").AbsoluteUri)), Cancellation);

        LibraryListing library = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("source", "fake-handoff")), Cancellation);
        LibraryListing missing = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("missingOnly", true)), Cancellation);
        ModDetails mod = await mcp.CallAsync<ModDetails>("mod_show", Arguments(("link", FileLink("3").AbsoluteUri)), Cancellation);
        LibraryChanges changes = await ChangesAsync(mcp, start);

        LibraryModEntry entry = Assert.Single(library.Mods);
        Assert.Equal((3, 1), (entry.FileCount, entry.MissingCount));
        Assert.Equal([("3", true), ("2", false), ("1", false)], entry.Files!.Select(file => (file.FileId, file.Missing)));
        Assert.Equal([("3", true), ("2", false), ("1", false)], mod.Files.Select(file => (file.FileId, file.Missing)));
        Assert.Equal([("1", false), ("2", false), ("3", true)], changes.Files.Select(file => (file.File.FileId, file.File.Missing)));

        // The watched mod without files has none missing.
        Assert.Equal(("mod", 1), (Assert.Single(missing.Mods).ModId, missing.Total));
    }

    [Fact]
    public async Task ListsALargeLibraryAPageAGameOrAModAtATime()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        foreach (string catalogMod in new[] { "1", "3", "5" })
        {
            await mcp.CallAsync<WatchResult>("library_watch", Arguments(("link", FakeCatalogSite.ModLink(catalogMod).AbsoluteUri)), Cancellation);
        }

        await DownloadAndWaitAsync(mcp, "1");
        await mcp.CallAsync<UpdateCheckResult>("library_check", null, Cancellation);
        await mcp.CallAsync<WatchResult>("library_unwatch", Arguments(("link", FakeCatalogSite.ModLink("5").AbsoluteUri)), Cancellation);

        LibraryListing first = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("limit", 2)), Cancellation);
        LibraryListing second = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("limit", 2), ("offset", 2)), Cancellation);
        LibraryListing compact = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("compact", true)), Cancellation);
        LibraryListing whole = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("limit", 3)), Cancellation);
        LibraryListing site = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("source", "FAKE-catalog"), ("game", "Game")), Cancellation);
        LibraryListing elsewhere = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("game", "elsewhere")), Cancellation);
        LibraryListing one = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("mod", "3")), Cancellation);
        LibraryListing updated = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("updatesOnly", true)), Cancellation);
        LibraryListing missing = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("missingOnly", true)), Cancellation);
        await mcp.CallAsync<WatchResult>("library_unwatch", Arguments(("link", FileLink("1").AbsoluteUri)), Cancellation);
        LibraryListing watched = await mcp.CallAsync<LibraryListing>("library_list", Arguments(("watchedOnly", true)), Cancellation);

        // Mod 5 left the library with no files when it was unwatched; the handoff mod stays, unwatched, with its file.
        Assert.Equal((3, 0, true), (first.Total, first.Offset, first.HasMore));
        Assert.Equal((3, 2, false), (second.Total, second.Offset, second.HasMore));
        Assert.Equal(["1", "3", "mod"], first.Mods.Concat(second.Mods).Select(mod => mod.ModId));
        Assert.All(compact.Mods, mod => Assert.Equal((null, null), (mod.Files, mod.Updates)));
        Assert.Equal((1, 1), (compact.Mods[2].FileCount, compact.Mods[2].UpdateCount));
        Assert.Equal((3, false), (whole.Mods.Count, whole.HasMore));
        Assert.Equal(["1", "3"], site.Mods.Select(mod => mod.ModId));
        Assert.Equal((0, false), (elsewhere.Total, elsewhere.HasMore));
        Assert.Equal("3", Assert.Single(one.Mods).ModId);
        Assert.Equal(["1", "3"], watched.Mods.Select(mod => mod.ModId));
        Assert.Equal(("mod", "3"), (Assert.Single(updated.Mods).ModId, Assert.Single(updated.Mods[0].Updates!).FileId));
        Assert.Equal((0, false), (missing.Total, missing.HasMore));
    }

    [Theory]
    [InlineData("limit", 0, "limit must be from 1 to 200.")]
    [InlineData("limit", 201, "limit must be from 1 to 200.")]
    [InlineData("offset", -1, "offset must be 0 or more.")]
    public async Task APageItCantGiveIsRefused(string name, int value, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        Assert.EndsWith(problem, await mcp.CallFailingAsync("library_list", Arguments((name, value)), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesAGamesMods()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        SearchResult found = await mcp.CallAsync<SearchResult>(
            "mod_search",
            Arguments(("game", "game"), ("source", "fake-catalog"), ("author", "Someone"), ("page", 2), ("pageSize", 5), ("sort", "downloads")),
            Cancellation);

        // Of mods 101 to 145, the even ones are by Someone.
        Assert.Equal((22, 5, true), (found.Total, found.Offset, found.HasMore));
        Assert.Equal(5, found.Mods.Count);
        Assert.All(found.Mods, mod => Assert.Equal(("fake-catalog", "game", "Someone"), (mod.Source, mod.Game, mod.Author)));
    }

    [Fact]
    public async Task FindsASitesGameByTheStartsOfItsNamesWords()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        GamesResult found = await mcp.CallAsync<GamesResult>("mod_games", Arguments(("words", "GAME example"), ("source", "fake-catalog")), Cancellation);
        GamesResult none = await mcp.CallAsync<GamesResult>("mod_games", Arguments(("words", "ample"), ("source", "fake-catalog")), Cancellation);
        GamesResult every = await mcp.CallAsync<GamesResult>("mod_games", Arguments(("source", "fake-catalog")), Cancellation);

        Assert.Equal((1, 0, false), (found.Total, found.Offset, found.HasMore));
        Assert.Equal(new GameEntry("fake-catalog", "game", "Example Game", "https://catalog.example/game"), Assert.Single(found.Games));
        Assert.Equal((0, 0), (none.Total, none.Games.Count));
        Assert.Equal(1, every.Total);
    }

    [Fact]
    public async Task ListsASitesGamesAPageAtATimeThoseStartingWithTheWordsFirst()
    {
        _catalog.MoreGames.AddRange([new GameInfo(new GameRef(FakeCatalogSite.SourceId, "gamebox"), "Game Box"), new GameInfo(new GameRef(FakeCatalogSite.SourceId, "zoo"), "Zoo")]);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        GamesResult first = await mcp.CallAsync<GamesResult>("mod_games", Arguments(("words", "game"), ("source", "fake-catalog"), ("pageSize", 1)), Cancellation);
        GamesResult second = await mcp.CallAsync<GamesResult>("mod_games", Arguments(("words", "game"), ("source", "fake-catalog"), ("pageSize", 1), ("page", 2)), Cancellation);

        Assert.Equal((2, 0, true, "gamebox"), (first.Total, first.Offset, first.HasMore, Assert.Single(first.Games).Game));
        Assert.Equal((2, 1, false, "game"), (second.Total, second.Offset, second.HasMore, Assert.Single(second.Games).Game));
    }

    [Theory]
    [InlineData("pageSize", 0, "pageSize must be from 1 to 100.")]
    [InlineData("pageSize", 101, "pageSize must be from 1 to 100.")]
    [InlineData("page", 0, "page must be 1 or more")]
    [InlineData("source", "direct", "'direct' can't list its games. Sites that can: ")]
    public async Task AListOfGamesItCantMakeIsRefused(string name, object value, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        Dictionary<string, object?> arguments = new() { ["source"] = "fake-catalog", [name] = value };

        Assert.Contains(problem, await mcp.CallFailingAsync("mod_games", arguments, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesInTheSitesOwnOrderAndPageSizeUnlessAsked()
    {
        _catalog.Capabilities = new([.. SearchOrder.BothWays(SearchSort.Newest), .. SearchOrder.BothWays(SearchSort.Name)], SearchFilters.Text, 10);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        SearchResult found = await mcp.CallAsync<SearchResult>("mod_search", Arguments(("game", "game"), ("source", "fake-catalog")), Cancellation);
        SearchQuery own = _catalog.LastQuery!;
        await mcp.CallAsync<SearchResult>("mod_search", Arguments(("game", "game"), ("source", "fake-catalog"), ("ascending", true), ("page", 2)), Cancellation);
        SearchQuery turned = _catalog.LastQuery!;

        Assert.Equal((44, 0, true, 10), (found.Total, found.Offset, found.HasMore, found.Mods.Count));
        Assert.Equal((SearchSort.Newest, SearchDirection.Descending, 10), (own.Sort, own.Direction, own.Count));
        Assert.Equal((SearchSort.Newest, SearchDirection.Ascending, 10, 10), (turned.Sort, turned.Direction, turned.Offset, turned.Count));
    }

    [Fact]
    public async Task TheSitesOwnOrderGoesItsOwnWayAndAscendingTurnsItAround()
    {
        _catalog.Capabilities = new([new(SearchSort.Name, SearchDirection.Ascending), new(SearchSort.Name, SearchDirection.Descending)], SearchFilters.Text, 100);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        await mcp.CallAsync<SearchResult>("mod_search", Arguments(("game", "game"), ("source", "fake-catalog")), Cancellation);
        SearchQuery own = _catalog.LastQuery!;
        await mcp.CallAsync<SearchResult>("mod_search", Arguments(("game", "game"), ("source", "fake-catalog"), ("ascending", true)), Cancellation);
        SearchQuery turned = _catalog.LastQuery!;

        Assert.Equal((SearchSort.Name, SearchDirection.Ascending), (own.Sort, own.Direction));
        Assert.Equal((SearchSort.Name, SearchDirection.Descending), (turned.Sort, turned.Direction));
    }

    [Theory]
    [InlineData("author", "Someone", "Fake catalog site can't search by author. It can search by words.")]
    [InlineData("pageSize", 100, "Fake catalog site lists at most 50 mods a page.")]
    [InlineData("sort", "Size", "Fake catalog site can't sort by Size. It can sort by Relevance.")]
    public async Task ASearchTheSiteCantDoIsRefusedSayingWhatItCan(string name, object value, string problem)
    {
        _catalog.Capabilities = new([new(SearchSort.Relevance, SearchDirection.Descending)], SearchFilters.Text, 50);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        Dictionary<string, object?> arguments = new() { ["game"] = "game", ["source"] = "fake-catalog", [name] = value };

        Assert.Contains(problem, await mcp.CallFailingAsync("mod_search", arguments, Cancellation), StringComparison.Ordinal);
        Assert.Null(_catalog.LastQuery);
    }

    [Theory]
    [InlineData("sort", "Popularity", "sort must be one of Relevance, Endorsements, Downloads, Updated, Newest, Name, Size, UniqueDownloads.")]
    [InlineData("sort", "9", "sort must be one of")]
    [InlineData("pageSize", 0, "pageSize must be from 1 to 100.")]
    [InlineData("page", 0, "page must be 1 or more")]
    [InlineData("source", "fake-handoff", "'fake-handoff' can't search. Sites that can: ")]
    public async Task ASearchItCantMakeIsRefused(string name, object value, string problem)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        Dictionary<string, object?> arguments = new() { ["game"] = "game", ["source"] = "fake-catalog", [name] = value };

        Assert.Contains(problem, await mcp.CallFailingAsync("mod_search", arguments, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhichAccountsAreLinkedButNotWhoseOrTheirKeys()
    {
        Assert.Equal(Done, (await Cli.RunAsync(["account", "link", FakeApiKeySource.SourceId.Value], Cancellation, _site.ApiKey + "\n")).ExitCode);
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        AccountsListing accounts = await mcp.CallAsync<AccountsListing>("accounts_status", null, Cancellation);
        DiagnosticsReport report = await mcp.CallAsync<DiagnosticsReport>("diagnostics", null, Cancellation);

        AccountEntry linked = Assert.Single(accounts.Accounts, account => account.Source == FakeApiKeySource.SourceId.Value);
        Assert.Equal((true, FakeAccountSite.KeyTier), (linked.Linked, linked.Level));
        Assert.Equal(["api-key"], linked.LinkWith);
        Assert.Contains(accounts.Accounts, account => account is { Source: "nexus", Linked: false, Level: null });
        string everything = string.Join('\n', (await mcp.CallRawAsync("accounts_status", null, Cancellation)).Content.Select(block => block.ToString())) + report.Report;
        Assert.DoesNotContain(FakeAccountSite.KeyUser, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(_site.ApiKey, everything, StringComparison.Ordinal);
        Assert.StartsWith("ModShelf ", report.Report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinksAndTheirKeysAreNeverLogged()
    {
        await using (McpSession mcp = await Cli.StartMcpAsync(Cancellation))
        {
            await DownloadAndWaitAsync(mcp, "3");
            Assert.NotNull((await DownloadAsync(mcp, "https://handoff.example/locked/1?key=" + GoodKey)).Problem);
            await mcp.CloseAsync(Cancellation);
        }

        Assert.Contains(Cli.Logs.Entries, entry => entry.Contains("The MCP server started, with 25 tools", StringComparison.Ordinal));
        Assert.DoesNotContain(Cli.Logs.Entries, entry => entry.Contains(GoodKey, StringComparison.Ordinal));
        Assert.DoesNotContain(Cli.Logs.Entries, entry => entry.Contains("handoff.example", StringComparison.Ordinal));
    }

    private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        => arguments.ToDictionary(argument => argument.Name, argument => argument.Value);

    private static async Task WaitUntilAsync(Func<Task<bool>> done, string otherwise)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        patience.CancelAfter(Patience);
        while (!await done())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), patience.Token);
            }
            catch (OperationCanceledException) when (!Cancellation.IsCancellationRequested)
            {
                Assert.Fail(otherwise);
            }
        }
    }

    /// <summary>
    /// Gives the mod updates, as a check would: Update 2, which replaces file 1, and the rest new optional files.
    /// </summary>
    private Task<Application.Library.LibraryChange> StoreUpdatesAsync(params string[] files)
    {
        ModRef mod = new(new GameRef(FakeHandoffSource.SourceId, "game"), "mod");
        return Cli.InAppAsync(
            services => services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                mod,
                stored => stored!.WithUpdates(
                [
                    .. files.Select(file => new LibraryUpdate(
                        new ModFileRef(mod, file),
                        $"Update {file}",
                        $"update-{file}.zip",
                        $"1.{file}",
                        file == "2" ? ModFileCategory.Main : ModFileCategory.Optional,
                        DateTimeOffset.UtcNow,
                        file == "2" ? new ModFileRef(mod, "1") : null,
                        DateTimeOffset.UtcNow,
                        FileLink(file))),
                ]),
                Cancellation),
            Cancellation);
    }

    private static Task<LibraryChanges> ChangesAsync(McpSession mcp, DateTimeOffset since)
        => mcp.CallAsync<LibraryChanges>("library_changes", Arguments(("since", since.ToString("O", System.Globalization.CultureInfo.InvariantCulture))), Cancellation);

    private static async Task<DownloadEntry> WaitForDownloadAsync(McpSession mcp, Func<DownloadEntry, bool> matches)
    {
        DownloadEntry? found = null;
        await WaitUntilAsync(
            async () => (found = (await mcp.CallAsync<DownloadListing>("downloads_list", null, Cancellation)).Downloads.FirstOrDefault(matches)) is not null,
            "No download came to that.");
        return found!;
    }

    private static async Task WaitForFileAsync(McpSession mcp, string fileId)
        => await WaitUntilAsync(
            async () => (await mcp.CallAsync<LibraryListing>("library_list", null, Cancellation)).Mods
                .Any(mod => mod.Source == FakeHandoffSource.SourceId.Value && mod.Files!.Any(file => file.FileId == fileId)),
            $"File {fileId} didn't come into the library.");

    /// <summary>
    /// Stands in a desktop app that runs the queue, and replies to a download of file 3 that it's queued, while the queue
    /// has it as <paramref name="status"/> already.
    /// </summary>
    /// <param name="inLibrary">False for a download the queue had already, of the same file to a folder outside the
    /// library, which the request goes on with.</param>
    private async Task DesktopAppHasAsync(DownloadJobStatus status, DownloadFailureReason? failure, bool inLibrary = true)
    {
        _desktopApp.Runs = true;
        DownloadJob stored = DownloadJob.Restore(
            DownloadJobId.New(), new ModFileRef(new ModRef(new GameRef(FakeHandoffSource.SourceId, "game"), "mod"), "3"), ModFolder, FileNameOf("3"), DateTimeOffset.UnixEpoch, status, 0, null, failure);
        await Cli.InAppAsync(
            async services =>
            {
                await services.GetRequiredService<IDownloadJobRepository>().AddAsync(stored, Cancellation);
                return stored;
            },
            Cancellation);
        _desktopApp.Reply = new QueueReply(
            QueueOutcome.Done,
            Job: new DownloadJobSnapshot(stored.Id, stored.File, ModFolder, FileNameOf("3"), DownloadJobStatus.Queued, 0, null, null, DateTimeOffset.UnixEpoch),
            LibraryPath: inLibrary ? Path.Combine(ModFolder, FileNameOf("3")) : null,
            AlreadyQueued: !inLibrary);
    }

    /// <summary>
    /// Downloads one link, and returns its result.
    /// </summary>
    private static async Task<LinkDownloadResult> DownloadAsync(McpSession mcp, string link)
    {
        DownloadsResult downloaded = await mcp.CallAsync<DownloadsResult>("download", Arguments(("links", new[] { link })), Cancellation);
        LinkDownloadResult result = Assert.Single(downloaded.Results);
        Assert.Equal(result.Message, downloaded.Message);
        Assert.Null(downloaded.Deprecated);
        return result;
    }

    private static async Task DownloadAndWaitAsync(McpSession mcp, string fileId)
    {
        await DownloadAsync(mcp, HandoffLink(fileId).AbsoluteUri);
        await WaitForFileAsync(mcp, fileId);
    }
}
