using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>settings show</c>, <c>set</c> and <c>reset</c>, with the settings file next to the test's database.
/// </summary>
public sealed class SettingsCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly string[] PreviewScreens = ["library", "updates", "browse", "downloads"];

    // A game's own settings as listed: its folders by their older name.
    private static readonly string[] StardewSettings =
    [
        "library.folders.nexus/StardewValley", "games.nexus/StardewValley.oldVersions", "games.nexus/StardewValley.checkUpdates",
        "games.nexus/StardewValley.updateInterval", "games.nexus/StardewValley.notify", "games.nexus/StardewValley.hidden",
        "games.nexus/StardewValley.browse.sort", "games.nexus/StardewValley.browse.direction", "games.nexus/StardewValley.browse.window",
        "games.nexus/StardewValley.browse.pageSize",
    ];

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public SettingsCommandTests()
    {
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string SettingsFile => Path.Combine(_cli.Folder, "settings.json");

    private string Mods => Path.Combine(_cli.Folder, "Mods");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ShowsTheDefaultsAndWhereTheSettingsAreKept()
    {
        CliResult shown = await RunAsync("show");

        Assert.Equal(Done, shown.ExitCode);
        Assert.Contains($"Settings are kept in {SettingsFile}.", shown.Output, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_cli.Downloads, "ModShelf") + " (default)", Value(shown, "library.root"));
        Assert.Equal("{source}/{game}/{mod} ({modid})/{file} (default)", Value(shown, "library.folders"));
        Assert.Equal("2 (default)", Value(shown, "library.oldVersions"));
        Assert.Equal("6h (default)", Value(shown, "updates.interval"));
        Assert.Equal("true (default)", Value(shown, "updates.notify"));
        Assert.Equal("system (default)", Value(shown, "appearance.theme"));
        Assert.Equal("ask (default)", Value(shown, "window.closeToTray"));
        Assert.Equal("false (default)", Value(shown, "setup.done"));
        Assert.Equal("true (default)", Value(shown, "app.checkForUpdates"));
        Assert.Equal(
            ["thumbnails (default)", "off (default)", "cards (default)", "off (default)"],
            PreviewScreens.Select(screen => Value(shown, "previews." + screen)));
        Assert.Equal("512 (default)", Value(shown, "pictures.maxSize"));
        Assert.Equal("blurred (default)", Value(shown, "pictures.adult"));
        Assert.Equal("1d (default)", Value(shown, "details.maxAge"));
        Assert.Equal("10 (default)", Value(shown, "quota.warnBelow"));
        Assert.Equal("false (default)", Value(shown, "agents.enabled"));
        Assert.Equal("47823 (default)", Value(shown, "agents.port"));
        Assert.Contains("To change a setting: settings set <setting> <value>", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsTheLibrarysRoot()
    {
        CliResult set = await RunAsync("set", "library.root", Mods + Path.DirectorySeparatorChar);

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal($"library.root is now {Mods}.", set.Output.Trim());
        Assert.Equal(Mods, Value(await RunAsync("show"), "library.root"));
        Assert.Contains("\"root\"", await File.ReadAllTextAsync(SettingsFile, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsFolderPatternsForEveryGameAndForOne()
    {
        CliResult every = await RunAsync("set", "library.folders", "{source}/{game}/{modid}/{file}");
        CliResult one = await RunAsync("set", "Library.Folders.SkyrimSpecialEdition", "{mod}/{file}");

        Assert.Equal("library.folders is now {source}/{game}/{modid}/{file}.", every.Output.Trim());
        Assert.Equal("library.folders.nexus/SkyrimSpecialEdition is now {mod}/{file}.", one.Output.Trim());
        CliResult shown = await RunAsync("show");
        Assert.Equal("{source}/{game}/{modid}/{file}", Value(shown, "library.folders"));
        Assert.Equal("{mod}/{file}", Value(shown, "library.folders.nexus/SkyrimSpecialEdition"));
    }

    [Fact]
    public async Task AGamesKeyAloneIsTheOnlySitesWhoseGamesKeysLookLikeIt()
    {
        string[] said =
        [
            (await RunAsync("set", "games.16951.notify", "true")).Output.Trim(),
            (await RunAsync("set", "games.fallout4.notify", "true")).Output.Trim(),
        ];

        Assert.StartsWith("games.gamebanana/16951.notify is now true", said[0], StringComparison.Ordinal);
        Assert.StartsWith("games.nexus/fallout4.notify is now true", said[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsAGamesOwnSettings()
    {
        string[] said =
        [
            (await RunAsync("set", "Games.StardewValley.Folders", "{mod}/{file}")).Output.Trim(),
            (await RunAsync("set", "games.nexus/stardewvalley.oldVersions", "archive")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.checkUpdates", "false")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.updateInterval", "1d")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.notify", "true")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.hidden", "true")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.browse.sort", "UniqueDownloads")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.browse.direction", "ascending")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.browse.window", "7d")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.browse.pageSize", "50")).Output.Trim(),
        ];

        Assert.Equal(
            [
                "games.nexus/StardewValley.folders is now {mod}/{file}.",
                "games.nexus/stardewvalley.oldVersions is now archive: when a newer version of a file is downloaded, the old ones move to an _archive folder.",
                "games.nexus/stardewvalley.checkUpdates is now false: nexus/stardewvalley's watched mods are checked for updates only when the game alone is checked.",
                "games.nexus/stardewvalley.updateInterval is now 1d: the desktop app checks nexus/stardewvalley's watched mods for updates that often.",
                "games.nexus/stardewvalley.notify is now true: the desktop app shows a notice when a background check finds new updates of nexus/stardewvalley's mods.",
                "games.nexus/stardewvalley.hidden is now true: the desktop app's lists leave nexus/stardewvalley out, and its mods are still checked for updates.",
                "games.nexus/stardewvalley.browse.sort is now uniquedownloads: the desktop app's Browse screen starts so when nexus/stardewvalley is chosen.",
                "games.nexus/stardewvalley.browse.direction is now ascending: the desktop app's Browse screen starts so when nexus/stardewvalley is chosen.",
                "games.nexus/stardewvalley.browse.window is now 7d: the desktop app's Browse screen starts so when nexus/stardewvalley is chosen.",
                "games.nexus/stardewvalley.browse.pageSize is now 50: the desktop app's Browse screen starts so when nexus/stardewvalley is chosen.",
            ],
            said);
        CliResult shown = await RunAsync("show");
        Assert.Equal(
            ["{mod}/{file}", "archive", "false", "1d", "true", "true", "uniquedownloads", "ascending", "7d", "50"],
            StardewSettings.Select(name => Value(shown, name)));
        Assert.Contains("\"games\"", await File.ReadAllTextAsync(SettingsFile, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsAGamesOwnPreviewsAndSetsThemBack()
    {
        string[] said =
        [
            (await RunAsync("set", "games.stardewvalley.previews", "Cards")).Output.Trim(),
            (await RunAsync("set", "games.nexus/stardewvalley.previews", " off ")).Output.Trim(),
            (await RunAsync("set", "games.stardewvalley.previews", "thumbnails")).Output.Trim(),
        ];
        CliResult shown = await RunAsync("show");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        CliResult refused = await RunAsync("set", "games.stardewvalley.previews", "big");
        CliResult reset = await RunAsync("reset", "games.stardewvalley.previews");

        Assert.Equal(
            [
                "games.nexus/stardewvalley.previews is now cards: on nexus/stardewvalley's page in the desktop app's Library, and in Browse while it's chosen, its mods show as cards, with large pictures.",
                "games.nexus/stardewvalley.previews is now off: on nexus/stardewvalley's page in the desktop app's Library, and in Browse while it's chosen, its mods show no pictures.",
                "games.nexus/stardewvalley.previews is now thumbnails: on nexus/stardewvalley's page in the desktop app's Library, and in Browse while it's chosen, each row of its mods starts with a small picture.",
            ],
            said);
        Assert.Equal("thumbnails", Value(shown, "games.nexus/stardewvalley.previews"));
        Assert.Contains("\"previews\": \"thumbnails\"", file, StringComparison.Ordinal);
        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("'big' isn't off, thumbnails or cards.", refused.Errors, StringComparison.Ordinal);
        Assert.Equal(
            "games.nexus/stardewvalley.previews is gone, so nexus/stardewvalley's mods show as previews.library and previews.browse say.",
            reset.Output.Trim());
        Assert.DoesNotContain("stardewvalley", (await RunAsync("show")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetsAGamesOwnSettingsBackToEveryGames()
    {
        await RunAsync("set", "games.fallout4.hidden", "true");
        await RunAsync("set", "games.fallout4.browse.pageSize", "100");

        string[] said =
        [
            (await RunAsync("reset", "games.fallout4.hidden")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.browse.pageSize")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.updateInterval")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.checkUpdates")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.notify")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.oldVersions")).Output.Trim(),
            (await RunAsync("reset", "games.fallout4.folders")).Output.Trim(),
        ];

        Assert.Equal(
            [
                "games.nexus/fallout4.hidden is gone, so the desktop app's lists show nexus/fallout4.",
                "games.nexus/fallout4.browse.pageSize is gone, so the desktop app's Browse screen shows nexus/fallout4's mods as it would any game's.",
                "games.nexus/fallout4.updateInterval is gone, so nexus/fallout4 goes by updates.interval again.",
                "games.nexus/fallout4.checkUpdates is gone, so nexus/fallout4's watched mods are checked for updates with every game's.",
                "games.nexus/fallout4.notify is gone, so nexus/fallout4 goes by updates.notify again.",
                "games.nexus/fallout4.oldVersions is gone, so nexus/fallout4 goes by library.oldVersions again.",
                "games.nexus/fallout4.folders is gone, so nexus/fallout4's files go by library.folders again.",
            ],
            said);
        Assert.DoesNotContain("fallout4", (await RunAsync("show")).Output, StringComparison.Ordinal);
        Assert.DoesNotContain("games", await File.ReadAllTextAsync(SettingsFile, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetsSettingsToTheirDefaults()
    {
        await RunAsync("set", "library.root", Mods);
        await RunAsync("set", "library.folders", "{modid}/{file}");
        await RunAsync("set", "library.folders.skyrimspecialedition", "{mod}/{file}");

        CliResult root = await RunAsync("reset", "library.root");
        CliResult game = await RunAsync("reset", "library.folders.skyrimspecialedition");
        CliResult folders = await RunAsync("reset", "library.folders");

        Assert.Equal($"library.root is back to its default, {Path.Combine(_cli.Downloads, "ModShelf")}.", root.Output.Trim());
        Assert.Equal("library.folders.nexus/skyrimspecialedition is gone, so nexus/skyrimspecialedition's files go by library.folders again.", game.Output.Trim());
        Assert.Equal("library.folders is back to its default, {source}/{game}/{mod} ({modid})/{file}.", folders.Output.Trim());
        CliResult shown = await RunAsync("show");
        Assert.EndsWith("(default)", Value(shown, "library.root"), StringComparison.Ordinal);
        Assert.DoesNotContain("skyrimspecialedition", shown.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("all", "library.oldVersions is now all: every version of a file stays.")]
    [InlineData("Archive", "library.oldVersions is now archive: when a newer version of a file is downloaded, the old ones move to an _archive folder.")]
    [InlineData("1", "library.oldVersions is now 1: when a newer version of a file is downloaded, the old ones are deleted.")]
    [InlineData("3", "library.oldVersions is now 3: when a newer version of a file is downloaded, the newest 3 stay and older ones are deleted.")]
    public async Task SetsWhatHappensToOldVersions(string value, string said)
    {
        CliResult set = await RunAsync("set", "Library.OldVersions", value);

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal(said, set.Output.Trim());
        Assert.Equal(value.ToLowerInvariant(), Value(await RunAsync("show"), "library.oldVersions"));
    }

    [Fact]
    public async Task ResetsWhatHappensToOldVersions()
    {
        await RunAsync("set", "library.oldVersions", "archive");

        CliResult reset = await RunAsync("reset", "library.oldVersions");

        Assert.Equal(
            "library.oldVersions is back to its default, 2: when a newer version of a file is downloaded, the newest 2 stay and older ones are deleted.",
            reset.Output.Trim());
        Assert.Equal("2 (default)", Value(await RunAsync("show"), "library.oldVersions"));
    }

    [Theory]
    [InlineData("set", "library.root", "Mods", "isn't a full path")]
    [InlineData("set", "library.folders", "{mod}", "The last part must be exactly {file}.")]
    [InlineData("set", "library.folders.skyrimspecialedition", "{name}/{file}", "{name} isn't a folder's part.")]
    [InlineData("set", "library.colour", "blue", "There's no setting called library.colour. Settings: library.root, library.folders, library.folders.<site>/<game>, library.oldVersions, updates.interval, updates.notify, appearance.theme, window.closeToTray, setup.done, app.checkForUpdates, previews.library, previews.updates, previews.browse, previews.downloads, pictures.maxSize, pictures.adult, details.maxAge, quota.warnBelow, agents.enabled, agents.port, games.<site>/<game>.root, games.<site>/<game>.folders, games.<site>/<game>.oldVersions, games.<site>/<game>.checkUpdates, games.<site>/<game>.updateInterval, games.<site>/<game>.notify, games.<site>/<game>.previews, games.<site>/<game>.hidden, games.<site>/<game>.browse.sort, games.<site>/<game>.browse.direction, games.<site>/<game>.browse.window, games.<site>/<game>.browse.pageSize, such as games.nexus/stardewvalley.notify; a game's key alone, as games.stardewvalley.notify, does while only one site's game has it.")]
    [InlineData("reset", "library.folders.", null, "There's no setting called library.folders.")]
    [InlineData("set", "library.oldVersions", "0", "'0' isn't a rule for old versions. Use all, archive, or how many versions to keep, from 1 to 99, such as 2.")]
    [InlineData("set", "appearance.theme", "blue", "'blue' isn't system, light or dark.")]
    [InlineData("set", "window.closeToTray", "yes", "'yes' isn't true or false.")]
    [InlineData("set", "previews.library", "big", "'big' isn't off, thumbnails or cards.")]
    [InlineData("set", "previews.history", "cards", "There's no setting called previews.history.")]
    [InlineData("set", "pictures.maxSize", "10", "'10' isn't a number of megabytes from 64 to 8192.")]
    [InlineData("set", "pictures.maxSize", "1GB", "'1GB' isn't a number of megabytes from 64 to 8192.")]
    [InlineData("set", "pictures.adult", "maybe", "'maybe' isn't hidden, blurred or shown.")]
    [InlineData("set", "details.maxAge", "8d", "'8d' isn't an interval.")]
    [InlineData("set", "quota.warnBelow", "91", "'91' isn't a percent from 0 to 90.")]
    [InlineData("set", "quota.warnBelow", "-1", "'-1' isn't a percent from 0 to 90.")]
    [InlineData("set", "agents.enabled", "yes", "'yes' isn't true or false.")]
    [InlineData("set", "agents.port", "80", "'80' isn't a port from 1024 to 65535.")]
    [InlineData("set", "agents.port", "65536", "'65536' isn't a port from 1024 to 65535.")]
    [InlineData("set", "games.fallout4.hidden", "yes", "'yes' isn't true or false.")]
    [InlineData("set", "games.fallout4.updateInterval", "10m", "'10m' isn't an interval.")]
    [InlineData("set", "games.fallout4.oldVersions", "none", "'none' isn't a rule for old versions.")]
    [InlineData("set", "games.fallout4.browse.sort", "best", "'best' isn't relevance, endorsements, downloads, updated, newest, name, size or uniquedownloads.")]
    [InlineData("set", "games.fallout4.browse.direction", "up", "'up' isn't ascending or descending.")]
    [InlineData("set", "games.fallout4.browse.window", "2d", "'2d' isn't 1d, 7d or 30d.")]
    [InlineData("set", "games.fallout4.browse.pageSize", "30", "'30' isn't 20, 50 or 100.")]
    [InlineData("set", "games.fallout4.colour", "teal", "There's no setting called games.fallout4.colour.")]
    [InlineData("set", "games..hidden", "true", "There's no setting called games..hidden.")]
    [InlineData("set", "games.a.b.hidden", "true", "There's no setting called games.a.b.hidden.")]
    [InlineData("set", "games.Nexus Mods/fallout4.hidden", "true", "'Nexus Mods/fallout4' isn't a game: name it by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("set", "games./fallout4.hidden", "true", "'/fallout4' isn't a game: name it by its site and its key there, such as nexus/stardewvalley.")]
    [InlineData("set", "library.folders.nexus/", "{mod}/{file}", "'nexus/' isn't a game: name it by its site and its key there, such as nexus/stardewvalley.")]
    public async Task SaysWhatsWrongWithASetting(string command, string name, string? value, string message)
    {
        CliResult refused = await RunAsync([command, name, .. value is null ? Array.Empty<string>() : [value]]);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", refused.Errors, StringComparison.Ordinal);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task SetsWhetherTheDesktopAppIsLightOrDark()
    {
        CliResult set = await RunAsync("set", "Appearance.Theme", " Dark ");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        string shownSet = Value(await RunAsync("show"), "appearance.theme");
        CliResult reset = await RunAsync("reset", "appearance.theme");

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal("appearance.theme is now dark: the desktop app is dark.", set.Output.Trim());
        Assert.Contains("\"theme\": \"dark\"", file, StringComparison.Ordinal);
        Assert.Equal("dark", shownSet);
        Assert.Equal(Done, reset.ExitCode);
        Assert.Equal("appearance.theme is back to its default, system: the desktop app is light or dark as the system is.", reset.Output.Trim());
        Assert.Equal("system (default)", Value(await RunAsync("show"), "appearance.theme"));
    }

    [Fact]
    public async Task SetsAScreensPreviewsAndResetsThem()
    {
        CliResult set = await RunAsync("set", "Previews.Library", " Cards ");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        string shownSet = Value(await RunAsync("show"), "previews.library");
        CliResult reset = await RunAsync("reset", "previews.library");

        Assert.Equal(Done, set.ExitCode);
        Assert.Equal("previews.library is now cards: the desktop app's Library screen shows its mods as cards, with large pictures.", set.Output.Trim());
        Assert.Contains("\"library\": \"cards\"", file, StringComparison.Ordinal);
        Assert.Equal("cards", shownSet);
        Assert.Equal("previews.library is back to its default, thumbnails: the desktop app's Library screen shows a small picture at the start of each row.", reset.Output.Trim());
        Assert.Equal("thumbnails (default)", Value(await RunAsync("show"), "previews.library"));
    }

    [Fact]
    public async Task AllowsAgentsAndSetsTheirPortAndResetsBoth()
    {
        CliResult allowed = await RunAsync("set", "agents.enabled", "true");
        CliResult port = await RunAsync("set", "agents.port", "50000");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        CliResult off = await RunAsync("set", "agents.enabled", "false");
        CliResult reset = await RunAsync("reset", "agents.enabled");
        CliResult portReset = await RunAsync("reset", "agents.port");

        Assert.Equal("agents.enabled is now true: AI agents may use ModShelf through its MCP server.", allowed.Output.Trim());
        Assert.Equal("agents.port is now 50000: while agents may use ModShelf, the desktop app serves its MCP server at http://127.0.0.1:50000/mcp.", port.Output.Trim());
        Assert.Contains("\"agents\": {\n    \"enabled\": true,\n    \"port\": 50000\n  }", file.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal("agents.enabled is now false: ModShelf's MCP server answers every agent's call saying agents are off.", off.Output.Trim());
        Assert.Equal("agents.enabled is back to its default, false: ModShelf's MCP server answers every agent's call saying agents are off.", reset.Output.Trim());
        Assert.Equal("agents.port is back to its default, 47823: while agents may use ModShelf, the desktop app serves its MCP server at http://127.0.0.1:47823/mcp.", portReset.Output.Trim());
        Assert.DoesNotContain("agents", await File.ReadAllTextAsync(SettingsFile, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsTheQuotaWarningTurnsItOffAndResetsIt()
    {
        CliResult set = await RunAsync("set", "quota.warnBelow", "25%");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        CliResult off = await RunAsync("set", "quota.warnBelow", "0");
        CliResult reset = await RunAsync("reset", "quota.warnBelow");

        Assert.Equal("quota.warnBelow is now 25: the desktop app warns when fewer than 25% of a site's requests for the hour or the day are left.", set.Output.Trim());
        Assert.Contains("\"quota\": {\n    \"warnBelow\": 25\n  }", file.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal("quota.warnBelow is now 0: the desktop app doesn't warn when a site's requests run low.", off.Output.Trim());
        Assert.Equal("quota.warnBelow is back to its default, 10: the desktop app warns when fewer than 10% of a site's requests for the hour or the day are left.", reset.Output.Trim());
        Assert.Equal("10 (default)", Value(await RunAsync("show"), "quota.warnBelow"));
    }

    [Fact]
    public async Task SetsThePicturesRoomAdultPicturesAndTheDetailsAgeAndResetsThem()
    {
        CliResult size = await RunAsync("set", "pictures.maxSize", "1024");
        CliResult adult = await RunAsync("set", "pictures.adult", "hidden");
        CliResult age = await RunAsync("set", "details.maxAge", "7d");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        CliResult sizeReset = await RunAsync("reset", "pictures.maxSize");
        CliResult adultReset = await RunAsync("reset", "pictures.adult");
        CliResult ageReset = await RunAsync("reset", "details.maxAge");

        Assert.Equal("pictures.maxSize is now 1024: saved pictures take at most 1024 MB, the oldest going first.", size.Output.Trim());
        Assert.Equal("pictures.adult is now hidden: the desktop app shows no picture for mods with adult content.", adult.Output.Trim());
        Assert.Equal("details.maxAge is now 7d: saved mod details are read from their site again once they're 7d old.", age.Output.Trim());
        foreach (string written in new[] { "\"maxSize\": 1024", "\"adult\": \"hidden\"", "\"maxAge\": \"7d\"" })
        {
            Assert.Contains(written, file, StringComparison.Ordinal);
        }

        Assert.Equal("pictures.maxSize is back to its default, 512: saved pictures take at most 512 MB, the oldest going first.", sizeReset.Output.Trim());
        Assert.Equal("pictures.adult is back to its default, blurred: the desktop app shows the pictures of mods with adult content blurred.", adultReset.Output.Trim());
        Assert.Equal("details.maxAge is back to its default, 1d: saved mod details are read from their site again once they're 1d old.", ageReset.Output.Trim());
        Assert.Equal("{}", (await File.ReadAllTextAsync(SettingsFile, Cancellation)).Trim());
    }

    [Fact]
    public async Task SetsWhetherClosingTheWindowKeepsModShelfInTheTray()
    {
        CliResult kept = await RunAsync("set", "Window.CloseToTray", "true");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        CliResult quits = await RunAsync("set", "window.closeToTray", "False");
        string shown = Value(await RunAsync("show"), "window.closeToTray");
        CliResult reset = await RunAsync("reset", "window.closeToTray");

        Assert.Equal(Done, kept.ExitCode);
        Assert.Equal("window.closeToTray is now true: closing the desktop app's window keeps ModShelf running in the tray.", kept.Output.Trim());
        Assert.Contains("\"closeToTray\": true", file, StringComparison.Ordinal);
        Assert.Equal("window.closeToTray is now false: closing the desktop app's window quits ModShelf.", quits.Output.Trim());
        Assert.Equal("false", shown);
        Assert.Equal("window.closeToTray is back to its default: the desktop app asks the first time its window closes.", reset.Output.Trim());
        Assert.Equal("ask (default)", Value(await RunAsync("show"), "window.closeToTray"));
    }

    [Fact]
    public async Task SetsWhetherToCheckForANewerModShelf()
    {
        CliResult off = await RunAsync("set", "App.CheckForUpdates", "false");
        string shown = Value(await RunAsync("show"), "app.checkForUpdates");
        CliResult reset = await RunAsync("reset", "app.checkForUpdates");

        Assert.Equal(Done, off.ExitCode);
        Assert.Equal("app.checkForUpdates is now false: the installed desktop app looks for a newer ModShelf only when asked, on its Settings screen.", off.Output.Trim());
        Assert.Equal("false", shown);
        Assert.Equal("app.checkForUpdates is back to its default, true: the installed desktop app looks for a newer ModShelf when it starts.", reset.Output.Trim());
    }

    [Fact]
    public async Task SetsWhetherTheFirstRunSetupIsDoneAndResetsIt()
    {
        CliResult done = await RunAsync("set", "setup.done", "true");
        string file = await File.ReadAllTextAsync(SettingsFile, Cancellation);
        string shown = Value(await RunAsync("show"), "setup.done");
        CliResult reset = await RunAsync("reset", "Setup.Done");

        Assert.Equal(Done, done.ExitCode);
        Assert.Equal("setup.done is now true: the desktop app doesn't show its first-run setup.", done.Output.Trim());
        Assert.Contains("\"setup\": {", file, StringComparison.Ordinal);
        Assert.Equal("true", shown);
        Assert.Equal("setup.done is back to its default, false: a new library shows the desktop app's first-run setup at its next start.", reset.Output.Trim());
        Assert.Equal("false (default)", Value(await RunAsync("show"), "setup.done"));
    }

    [Fact]
    public async Task KeepsTheRootWhileTheLibraryHasFiles()
    {
        await _cli.InAppAsync(
            services =>
            {
                ModRef mod = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
                LibraryFile file = new(
                    new ModFileRef(mod, "160380"), "SMAPI 4.5.2", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, "nexus/stardewvalley/SMAPI (2400)/SMAPI.zip", 1, DateTimeOffset.UnixEpoch);
                return services.GetRequiredService<ILibraryRepository>().UpdateAsync(
                    mod, _ => LibraryMod.Watch(mod, "SMAPI", null, null, DateTimeOffset.UnixEpoch).WithFile(file), Cancellation);
            },
            Cancellation);

        CliResult refused = await RunAsync("set", "library.root", Mods);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Contains($"The library has files in {Path.Combine(_cli.Downloads, "ModShelf")}.", refused.Errors, StringComparison.Ordinal);
        Assert.EndsWith($"To move it there: library move \"{Mods}\"", refused.Errors.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSettingsFileCantBeRead()
    {
        await File.WriteAllTextAsync(SettingsFile, "{ not json", Cancellation);

        CliResult shown = await RunAsync("show");
        CliResult set = await RunAsync("set", "library.folders", "{mod}/{file}");

        Assert.Equal((Failed, Failed), (shown.ExitCode, set.ExitCode));
        Assert.Contains($"The settings in {SettingsFile} can't be read:", shown.Errors, StringComparison.Ordinal);
        Assert.Contains("Fix the file, or delete it to start again.", set.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSettingsFileCantBeOpened()
    {
        Directory.CreateDirectory(SettingsFile);

        CliResult shown = await RunAsync("show");
        CliResult set = await RunAsync("set", "updates.notify", "false");

        Assert.Equal((Failed, Failed), (shown.ExitCode, set.ExitCode));
        Assert.StartsWith($"The settings in {SettingsFile} can't be read:", shown.Errors, StringComparison.Ordinal);
        Assert.StartsWith($"The settings in {SettingsFile} can't be read:", set.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheSettingsCantBeSaved()
    {
        await using InProcessCli cli = new(_site, _browser, configureServices: services =>
        {
            services.RemoveAll<ISettingsStore>();
            services.AddSingleton<ISettingsStore>(new ScriptedSettingsStore(
                _ => Task.FromResult(new AppSettings()),
                (_, _) => Task.FromException(new IOException("The settings couldn't be saved to settings.json: The disk is full."))));
        });

        CliResult refused = await cli.RunAsync(["settings", "set", "updates.notify", "false"], Cancellation);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal("The settings couldn't be saved to settings.json: The disk is full.", refused.Errors.Trim());
    }

    /// <summary>
    /// The value <c>settings show</c> gives a setting.
    /// </summary>
    private static string Value(CliResult shown, string setting)
    {
        string row = Assert.Single(shown.Output.ReplaceLineEndings("\n").Split('\n'), line => line.StartsWith(setting + " ", StringComparison.Ordinal));
        return row[setting.Length..].Trim();
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(["settings", .. arguments], Cancellation);
}
