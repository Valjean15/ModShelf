using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// mod.io's games open to apps, a game, a mod, its files and what each version changed, as recorded.
/// </summary>
public sealed class ModIoSourceCatalogTests : IDisposable
{
    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheGamesOpenToAppsReadingEveryPage()
    {
        _harness.Api.ServeAll();

        IReadOnlyList<GameInfo> games = await _harness.Source.GetGamesAsync(ModIoHarness.Cancellation);

        // 390 games in pages of 100, the two not recorded holding none.
        Assert.Equal(
            [0, 100, 200, 300],
            _harness.Asked.Select(asked => int.Parse(asked.Split("_offset=")[1], System.Globalization.CultureInfo.InvariantCulture)).Order());
        Assert.All(_harness.Asked, asked => Assert.StartsWith("/v1/games?api_access_options-bitwise-and=1&_sort=name&_limit=100&_offset=", asked, StringComparison.Ordinal));
        Assert.Equal(200, games.Count);
        GameInfo baldurs = Assert.Single(games, game => game.Game == ModIoHarness.Game);
        Assert.Equal(("Baldur's Gate 3", new Uri("https://mod.io/g/baldursgate3")), (baldurs.Name, baldurs.PageUrl));
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains("Read mod.io's list of games open to apps: 200 games in 4 pages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeavesOutAGameClosedToAppsOrWithoutAName()
    {
        _harness.Api.Answer("/v1/games", answer => answer.WithBody("""
            {"data": [
              {"id": 1, "name": "Open", "api_access_options": 3},
              {"id": 2, "name": "Closed", "api_access_options": 2},
              {"id": 3, "name": " ", "api_access_options": 1},
              {"id": 1, "name": "Open", "api_access_options": 1}
            ], "result_total": 4}
            """));

        IReadOnlyList<GameInfo> games = await _harness.Source.GetGamesAsync(ModIoHarness.Cancellation);

        Assert.Equal(["1"], games.Select(game => game.Game.Key));
        Assert.Single(_harness.Api.Requests);
    }

    [Theory]
    [InlineData("6715", true)]
    [InlineData("baldursgate3", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void ItsGamesKeysAreNumbers(string key, bool could)
    {
        Assert.Equal(could, _harness.Source.CouldBeGameKey(key));
    }

    [Fact]
    public async Task DescribesAGameWithItsPicturesAndItsTagsAsCategories()
    {
        _harness.Api.Serve("game");

        GameInfo game = await _harness.Source.GetGameAsync(ModIoHarness.Game, ModIoHarness.Cancellation);

        Assert.Equal(["/v1/games/6715"], _harness.Asked);
        Assert.Equal(("Baldur's Gate 3", new Uri("https://mod.io/g/baldursgate3")), (game.Name, game.PageUrl));
        Assert.Equal(new Uri("https://thumb.modcdn.io/games/38fa/6715/crop_1280x720/bg3_keyart_master_horitontal_fi.jpg"), game.CoverUrl);
        Assert.Equal(new Uri("https://thumb.modcdn.io/games/38fa/6715/crop_128x128/bg3-icon.png"), game.IconUrl);
        Assert.Equal(["Cheats", "Classes", "Customisation"], game.Categories.Take(3));
        Assert.Contains("Available on Mac and Console", game.Categories);
        Assert.Contains("Winter", game.Categories);
    }

    [Fact]
    public async Task LeavesOutTheTagsOnlyAGamesAdminsSee()
    {
        _harness.Api.Answer("/v1/games/6715", answer => answer.WithBody("""
            {"id": 6715, "name": "Game", "tag_options": [
              {"name": "Type", "hidden": false, "tags": ["Maps", "Tools"]},
              {"name": "Staff", "hidden": true, "tags": ["Featured"]},
              {"name": "Language", "tags": ["English", "Maps"]}
            ]}
            """));

        GameInfo game = await _harness.Source.GetGameAsync(ModIoHarness.Game, ModIoHarness.Cancellation);

        Assert.Equal(["Maps", "Tools", "English"], game.Categories);
        Assert.Equal(((Uri?)null, (Uri?)null, (Uri?)null), (game.PageUrl, game.CoverUrl, game.IconUrl));
    }

    [Fact]
    public async Task AGameThatDoesntExistIsNotFound()
    {
        _harness.Api.Serve("game-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.GetGameAsync(new GameRef(ModIoHarness.ModIo, "999999999"), ModIoHarness.Cancellation));

        Assert.Equal((HttpStatusCode.NotFound, "mod.io has no such game, mod or file."), (refused.StatusCode, refused.Message));
    }

    [Fact]
    public async Task DescribesAModWithItsLiveFileForWindows()
    {
        _harness.Api.ServeAll();

        ModInfo mod = await _harness.Source.GetModAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods/4228735", Assert.Single(_harness.Asked));
        IDictionary<string, WireMock.Types.WireMockList<string>> headers = Assert.Single(_harness.Api.Requests).RequestMessage!.Headers!;
        Assert.Equal("windows", headers["X-Modio-Platform"].Single());
        Assert.Equal("application/json", headers["Accept"].Single());
        Assert.StartsWith("ModShelf/", headers["User-Agent"].Single(), StringComparison.Ordinal);
        Assert.Equal(("Better Inventory UI", "Caites", "1.4.4.21", 471507L), (mod.Name, mod.Author, mod.Version, mod.Size));
        Assert.StartsWith("Makes inventories easier to read and organize.", mod.Summary, StringComparison.Ordinal);
        Assert.Equal(new Uri("https://mod.io/g/baldursgate3/m/better-inventory-ui1"), mod.PageUrl);
        Assert.Equal(new Uri("https://thumb.modcdn.io/mods/7d37/4228735/crop_1280x720/better_inv_ui_final.2.png"), mod.PictureUrl);
        Assert.Equal(new Uri("https://thumb.modcdn.io/mods/7d37/4228735/crop_320x180/better_inv_ui_final.2.png"), mod.ThumbnailUrl);
        Assert.Equal((4858859L, 3813L), (mod.Downloads, mod.Endorsements));
        Assert.Equal(
            (DateTimeOffset.FromUnixTimeSeconds(1759143222), DateTimeOffset.FromUnixTimeSeconds(1722713249)),
            (mod.UpdatedAt, mod.PublishedAt));
        Assert.Equal(("Quality of Life", true, false), (mod.Category, mod.IsAvailable, mod.HasAdultContent));
    }

    [Theory]
    [InlineData(1, 1, 8, true, true)]
    [InlineData(1, 1, 12, true, true)]
    [InlineData(1, 1, 7, true, false)]
    [InlineData(0, 1, 0, false, false)]
    [InlineData(1, 0, 0, false, false)]
    [InlineData(3, 1, 0, false, false)]
    public async Task AModIsAvailableWhenAcceptedAndPublicAndAdultWhenExplicit(int status, int visible, int maturity, bool available, bool adult)
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            $$"""{"id": 4228735, "game_id": 6715, "name": "Mod", "status": {{status}}, "visible": {{visible}}, "maturity_option": {{maturity}}, "modfile": {} }"""));

        ModInfo mod = await _harness.Source.GetModAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal((available, adult), (mod.IsAvailable, mod.HasAdultContent));
        Assert.Equal(((string?)null, (long?)null, (string?)null), (mod.Version, mod.Size, mod.Category));
    }

    [Fact]
    public async Task ALiveFileWithoutASizeGivesTheModNone()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 1, "filesize": 0, "version": "1.0"}}"""));

        ModInfo mod = await _harness.Source.GetModAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(("1.0", (long?)null), (mod.Version, mod.Size));
        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null), (mod.UpdatedAt, mod.PublishedAt));
    }

    [Fact]
    public async Task AModsCategoryIsItsFirstTagWithAName()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "tags": [{"name": " "}, {}, {"name": " UI "}, {"name": "Tools"}]}"""));

        ModInfo mod = await _harness.Source.GetModAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal("UI", mod.Category);
    }

    [Fact]
    public async Task AModThatDoesntExistIsNotFound()
    {
        _harness.Api.Answer("/v1/games/6715/mods/999999999", "mod-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.GetModAsync(new ModRef(ModIoHarness.Game, "999999999"), ModIoHarness.Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task AGameClosedToAppsIsForbidden()
    {
        _harness.Api.Answer("/v1/games/304/mods", "game-closed-mods");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => _harness.Source.SearchAsync(new GameRef(ModIoHarness.ModIo, RecordedModIoApi.ClosedGame), new Abstractions.Search.SearchQuery(), ModIoHarness.Cancellation));

        Assert.Equal(
            (HttpStatusCode.Forbidden, "mod.io doesn't let apps such as ModShelf see this game: it's closed to them, or hidden."),
            (refused.StatusCode, refused.Message));
    }

    [Fact]
    public async Task ListsTheVersionsForWindowsEachReplacedByTheNextUpToTheLiveOne()
    {
        _harness.Api.ServeAll();

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(["/v1/games/6715/mods/4228735", "/v1/games/6715/mods/4228735/files?_sort=-date_added&_limit=100"], _harness.Asked.Order());

        // Of the ten, one was never approved for any platform.
        Assert.Equal(9, files.Count);
        Assert.DoesNotContain(files, file => file.Reference.Id == "5525593");
        ModFile live = Assert.Single(files, file => file.Category == ModFileCategory.Main);
        Assert.Equal((RecordedModIoApi.LiveFile, "1.4.4.21", (ModFileRef?)null), (live.Reference.Id, live.VersionLabel, live.ReplacedBy));
        Assert.Equal(("betterinventoryui_6b585be8-ed7-bplo.zip", 471507L), (live.FileName, live.SizeInBytes));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758650113), live.UploadedAt);
        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, "e9be965442ab4ad811b9cb93f305f91b"), Assert.Single(live.Hashes));
        Assert.Equal(new Uri("https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/6795889/download"), live.PageUrl);

        // Following each old version's successor leads to the live one, version by version.
        Dictionary<string, ModFile> byId = files.ToDictionary(file => file.Reference.Id);
        Assert.Equal(RecordedModIoApi.LiveFile, byId[RecordedModIoApi.PreviousFile].ReplacedBy?.Id);
        Assert.Equal("5539156", byId["5525594"].ReplacedBy?.Id);
        Assert.Equal("5525594", byId["5523184"].ReplacedBy?.Id);
        Assert.All(files.Where(file => file != live), file => Assert.Equal(ModFileCategory.OldVersion, file.Category));
    }

    [Fact]
    public async Task AFileUploadedAfterTheLiveOneIsReplacedByIt()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 2}, "platforms": []}"""));
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody("""
            {"data": [
              {"id": 3, "date_added": 300, "filename": "c.zip"},
              {"id": 2, "date_added": 200, "filename": "b.zip"},
              {"id": 1, "date_added": 100, "filename": "a.zip", "platforms": [{"platform": "ps5", "status": 1}]}
            ], "result_total": 3}
            """));

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        // Without files per platform, every file is listed.
        Assert.Equal(
            [("1", ModFileCategory.OldVersion, "2"), ("2", ModFileCategory.Main, null), ("3", ModFileCategory.OldVersion, "2")],
            files.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
    }

    [Fact]
    public async Task WithoutALiveFileEachVersionIsReplacedByTheNext()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {}, "platforms": [{"platform": "ps5", "modfile_live": 2}]}"""));
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody("""
            {"data": [
              {"id": 2, "date_added": 200, "filename": "b.zip", "platforms": [{"platform": "ps5", "status": 1}, {"platform": "windows", "status": 1}]},
              {"id": 1, "date_added": 100, "filename": "a.zip", "platforms": [{"platform": "Windows", "status": 1}]},
              {"id": 4, "date_added": 50, "filename": "d.zip", "platforms": [{"platform": "windows", "status": 0}]},
              {"id": 5, "date_added": 60, "filename": "e.zip", "platforms": [{"platform": "windows", "status": 2}]}
            ], "result_total": 4}
            """));

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        // Pending and denied files aren't Windows' versions.
        Assert.Equal(
            [("1", ModFileCategory.OldVersion, "2"), ("2", ModFileCategory.OldVersion, null)],
            files.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
    }

    [Fact]
    public async Task VersionsFollowEachOtherByWhenTheyWereUploadedTheLiveOneWhateverItsPlatformsSay()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {}, "platforms": [{"platform": "windows", "modfile_live": 5}]}"""));
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody("""
            {"data": [
              {"id": 5, "date_added": 300, "platforms": [{"platform": "windows", "status": 0}]},
              {"id": 9, "date_added": 200, "platforms": [{"platform": "windows", "status": 1}]},
              {"id": 7, "date_added": 100, "platforms": [{"platform": "windows", "status": 1}], "filehash": {"md5": "not-a-checksum"}},
              {"id": 0, "date_added": 50, "platforms": [{"platform": "windows", "status": 1}]}
            ], "result_total": 4}
            """));

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(
            [("7", ModFileCategory.OldVersion, "9"), ("9", ModFileCategory.OldVersion, "5"), ("5", ModFileCategory.Main, null)],
            files.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
        Assert.All(files, file => Assert.Empty(file.Hashes));
    }

    [Fact]
    public async Task ReadsEveryPageOfFilesTheModHas()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 1}}"""));
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "date_added": {{id}} }"""))}}], "result_total": 101}""";
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody(page));

        IReadOnlyList<ModFile> files = await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(100, files.Count);
        Assert.Equal(
            ["/v1/games/6715/mods/4228735", "/v1/games/6715/mods/4228735/files?_sort=-date_added&_limit=100", "/v1/games/6715/mods/4228735/files?_sort=-date_added&_limit=100&_offset=100"],
            _harness.Asked.Order());
    }

    [Fact]
    public async Task APageOfFilesShortOfFullIsTheLast()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 1}}"""));
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody("""{"data": [{"id": 1}], "result_total": 3}"""));

        await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(2, _harness.Asked.Count);
    }

    [Fact]
    public async Task ReadsNoMoreThanFivePagesOfFiles()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 1}}"""));
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "date_added": {{id}} }"""))}}], "result_total": 10000}""";
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody(page));

        await _harness.Source.GetFilesAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(5, _harness.Asked.Count(asked => asked.Contains("/files", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EachVersionsChangesAreItsChangelogTheNewestFirst()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735", answer => answer.WithBody(
            """{"id": 4228735, "game_id": 6715, "name": "Mod", "modfile": {"id": 3}}"""));
        _harness.Api.Answer("/v1/games/6715/mods/4228735/files", answer => answer.WithBody("""
            {"data": [
              {"id": 3, "date_added": 300, "version": "1.2", "changelog": "Fixed the bags.\r\n\r\n Added a filter. "},
              {"id": 2, "date_added": 200, "version": "1.1", "changelog": " "},
              {"id": 1, "date_added": 100, "changelog": "First."}
            ], "result_total": 3}
            """));

        IReadOnlyList<ModChangelog> changelogs = await _harness.Source.GetChangelogsAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal([("1.2", "Fixed the bags.|Added a filter."), (string.Empty, "First.")], changelogs.Select(log => (log.Version, string.Join('|', log.Changes))));
    }

    [Fact]
    public async Task TheRecordedModListsNoChanges()
    {
        _harness.Api.ServeAll();

        Assert.Empty(await _harness.Source.GetChangelogsAsync(ModIoHarness.Mod, ModIoHarness.Cancellation));
    }

    [Theory]
    [InlineData("nexus", "6715", "4228735")]
    [InlineData("modio", "baldursgate3", "4228735")]
    [InlineData("modio", "6715", "better-inventory-ui1")]
    public async Task RefusesAModNamedOtherThanByItsIds(string source, string game, string mod)
    {
        ModRef named = new(new GameRef(new SourceId(source), game), mod);

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.GetFilesAsync(named, ModIoHarness.Cancellation));
        Assert.Empty(_harness.Api.Requests);
    }
}
