using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Games, mods, files and changelogs against the recorded API: Stardew Valley and SMAPI.
/// </summary>
public sealed class NexusSourceCatalogTests : IDisposable
{
    private const string ModPath = "/v1/games/stardewvalley/mods/2400.json";
    private const string FilesPath = "/v1/games/stardewvalley/mods/2400/files.json";
    private const string ChangelogsPath = "/v1/games/stardewvalley/mods/2400/changelogs.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");
    private static readonly ModRef Smapi = new(StardewValley, "2400");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceCatalogTests()
    {
        _services = _api.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public async Task AGameHasItsNameAndPage()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");

        GameInfo game = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Same(StardewValley, game.Game);
        Assert.Equal("Stardew Valley", game.Name);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley"), game.PageUrl);
    }

    [Fact]
    public async Task AGameAnswerWithoutANameOrPageStillDescribesTheGame()
    {
        _api.Answer("/v1/games/stardewvalley.json", response => response.WithStatusCode(200).WithBody(
            """{"id": 1303, "nexusmods_url": "ftp://www.nexusmods.com/stardewvalley"}"""));

        GameInfo game = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Equal("stardewvalley", game.Name);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley"), game.PageUrl);
    }

    [Fact]
    public async Task AGameThatDoesntExistIsNotFound()
    {
        _api.Serve(RecordedNexusApi.Premium, "game-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.GetGameAsync(new GameRef(new SourceId("nexus"), "notagame"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ListsEachGameWithItsNameAndPageInOneRequest()
    {
        JsonNode stardew = RecordedGame();
        JsonNode skyrim = RecordedGame();
        skyrim["name"] = "Skyrim Special Edition";
        skyrim["domain_name"] = "skyrimspecialedition";
        skyrim["nexusmods_url"] = "https://www.nexusmods.com/skyrimspecialedition";
        AnswerGames(stardew, skyrim);

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);

        Assert.Equal(
            [
                new GameInfo(StardewValley, "Stardew Valley", new Uri("https://www.nexusmods.com/stardewvalley")),
                new GameInfo(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "Skyrim Special Edition", new Uri("https://www.nexusmods.com/skyrimspecialedition")),
            ],
            games);
        Assert.Equal("/v1/games.json", Assert.Single(_api.Requests).RequestMessage!.Path);
    }

    [Fact]
    public async Task ListsTheGamesAsNexusModsListedThem()
    {
        _api.Serve(RecordedNexusApi.Premium, "games");

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);

        Assert.Equal(
            [("morrowind", "Morrowind"), ("oblivion", "Oblivion"), ("shadowrunreturns", "Shadowrun Returns"), ("stardewvalley", "Stardew Valley")],
            games.Select(game => (game.Game.Key, game.Name)));
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley"), games[^1].PageUrl);
    }

    [Fact]
    public async Task ListsEachGameOnceAndLeavesOutThoseWithoutADomainNameForLinks()
    {
        JsonNode again = RecordedGame();
        again["name"] = "Stardew Valley again";
        JsonNode shouting = RecordedGame();
        shouting["domain_name"] = "STARDEWVALLEY";
        AnswerGames(
            RecordedGame(),
            null,
            JsonNode.Parse("""{"name": "No domain"}"""),
            JsonNode.Parse("""{"name": "Spaces", "domain_name": "not a domain"}"""),
            JsonNode.Parse("""{"name": "Blank", "domain_name": ""}"""),
            again,
            shouting);

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);

        Assert.Equal("Stardew Valley", Assert.Single(games).Name);
    }

    [Fact]
    public async Task AListedGameWithoutANameOrPageGoesByItsDomainName()
    {
        AnswerGames(JsonNode.Parse("""{"domain_name": "NewGame", "nexusmods_url": "ftp://www.nexusmods.com/newgame"}"""));

        GameInfo game = Assert.Single(await Source.GetGamesAsync(Cancellation));

        Assert.Equal(new GameRef(new SourceId("nexus"), "newgame"), game.Game);
        Assert.Equal("newgame", game.Name);
        Assert.Equal(new Uri("https://www.nexusmods.com/newgame"), game.PageUrl);
    }

    [Fact]
    public async Task AGameListTheApiRefusesIsAnError()
    {
        _api.Answer("/v1/games.json", response => response.WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("Down"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetGamesAsync(Cancellation));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
    }

    [Fact]
    public async Task AModHasItsDetails()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");
        JsonNode recorded = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "mod")["response"]!["body"]!;

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Same(Smapi, mod.Mod);
        Assert.Equal(recorded["name"]!.GetValue<string>(), mod.Name);
        Assert.Equal(recorded["summary"]!.GetValue<string>(), mod.Summary);
        Assert.Equal(recorded["author"]!.GetValue<string>(), mod.Author);
        Assert.Equal(recorded["version"]!.GetValue<string>(), mod.Version);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), mod.PageUrl);
        Assert.Equal(new Uri(recorded["picture_url"]!.GetValue<string>()), mod.PictureUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(recorded["updated_timestamp"]!.GetValue<long>()), mod.UpdatedAt);
        Assert.True(mod.IsAvailable);
        Assert.False(mod.HasAdultContent);
        Assert.Equal(recorded["mod_downloads"]!.GetValue<long>(), mod.Downloads);
        Assert.Equal(recorded["endorsement_count"]!.GetValue<long>(), mod.Endorsements);
    }

    [Theory]
    [InlineData("""{"mod_id": 2400, "name": "SMAPI", "available": false, "status": "hidden"}""")]
    [InlineData("""{"mod_id": 2400, "name": "SMAPI", "available": false, "status": "published"}""")]
    [InlineData("""{"mod_id": 2400, "name": "SMAPI", "available": true, "status": "under_moderation"}""")]
    [InlineData("""{"mod_id": 2400, "available": false, "status": "removed"}""")]
    public async Task AHiddenOrRemovedModIsntAvailable(string answer)
    {
        _api.Answer(ModPath, response => response.WithStatusCode(200).WithBody(answer));

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.False(mod.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(mod.Name));
    }

    [Fact]
    public async Task AModAnswerWithLittleInItStillDescribesTheMod()
    {
        _api.Answer(ModPath, response => response.WithStatusCode(200).WithBody(
            """{"mod_id": 2400, "picture_url": "javascript:alert(1)", "contains_adult_content": true}"""));

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal("Mod 2400", mod.Name);
        Assert.Null(mod.PictureUrl);
        Assert.Null(mod.UpdatedAt);
        Assert.True(mod.HasAdultContent);
        Assert.True(mod.IsAvailable);
        Assert.Null(mod.Downloads);
        Assert.Null(mod.Endorsements);
    }

    [Fact]
    public async Task AModThatDoesntExistIsNotFound()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.GetModAsync(new ModRef(StardewValley, "999999999"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ListsEveryFileOfTheMod()
    {
        _api.Serve(RecordedNexusApi.Premium, "files");
        JsonArray recorded = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "files")["response"]!["body"]!["files"]!.AsArray();

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(Smapi, Cancellation);

        Assert.Equal(recorded.Count, files.Count);
        Assert.All(files, file => Assert.Same(Smapi, file.Reference.Mod));
        ModFile main = Assert.Single(files, file => file.Category == ModFileCategory.Main);
        Assert.Equal("160380", main.Reference.Id);
        Assert.Equal("SMAPI 4.5.2", main.Name);
        Assert.Equal("SMAPI 4.5.2-2400-4-5-2-1773515243.zip", main.FileName);
        Assert.Equal("4.5.2", main.VersionLabel);
        Assert.Equal(41889142, main.SizeInBytes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1773515243), main.UploadedAt);
        Assert.Empty(main.Hashes);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"), main.PageUrl);
        Assert.Equal(341, files.Count(file => file.Category == ModFileCategory.OldVersion));
        Assert.Equal(ModFileCategory.Unknown, Assert.Single(files, file => file.Reference.Id == "25256").Category);
    }

    [Theory]
    [InlineData(1, null, ModFileCategory.Main)]
    [InlineData(2, null, ModFileCategory.Update)]
    [InlineData(3, null, ModFileCategory.Optional)]
    [InlineData(4, null, ModFileCategory.OldVersion)]
    [InlineData(5, null, ModFileCategory.Miscellaneous)]
    [InlineData(6, null, ModFileCategory.Unknown)]
    [InlineData(7, "ARCHIVED", ModFileCategory.Archived)]
    [InlineData(1, "OPTIONAL", ModFileCategory.Optional)]
    [InlineData(3, "update", ModFileCategory.Update)]
    [InlineData(9, "SOMETHING_NEW", ModFileCategory.Unknown)]
    public async Task SortsFilesIntoTheSameSectionsAsTheWebsite(int categoryId, string? categoryName, ModFileCategory category)
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBodyAsJson(new
        {
            files = new[] { new { file_id = 1, name = "File", file_name = "file.zip", category_id = categoryId, category_name = categoryName, uploaded_timestamp = 1 } },
        }));

        Assert.Equal(category, Assert.Single(await Source.GetFilesAsync(Smapi, Cancellation)).Category);
    }

    [Fact]
    public async Task FillsInWhatAFileAnswerLeavesOut()
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBody("""
            {"files": [
              {"file_id": 1, "category_id": 1, "uploaded_timestamp": 1773515243, "size_kb": 2},
              {"file_id": 2, "category_id": 1, "uploaded_timestamp": 1773515243, "file_name": "Bad: name?.zip"}
            ]}
            """));

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(Smapi, Cancellation);

        Assert.Equal(("File 1", "1", 2048L), (files[0].Name, files[0].FileName, files[0].SizeInBytes!.Value));
        Assert.Equal(("Bad: name?.zip", "Bad_ name_.zip"), (files[1].Name, files[1].FileName));
        Assert.Null(files[1].SizeInBytes);
        Assert.Null(files[1].VersionLabel);
    }

    [Fact]
    public async Task FollowsTheUpdateChainsToTheNewestVersion()
    {
        _api.Serve(RecordedNexusApi.Premium, "files");

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(Smapi, Cancellation);

        Dictionary<string, ModFile> byId = files.ToDictionary(file => file.Reference.Id);
        Assert.Equal(new ModFileRef(Smapi, "11997"), byId["11397"].ReplacedBy);
        Assert.Null(byId["160380"].ReplacedBy);
        Assert.Equal(294, files.Count(file => file.ReplacedBy is not null));
        Assert.Equal(82, files.Count(file => NewestOf(file, byId) == "160380"));
    }

    [Fact]
    public async Task KeepsOnlyLinksBetweenTwoFiles()
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBody("""
            {"files": [
              {"file_id": 1, "category_id": 4, "uploaded_timestamp": 1},
              {"file_id": 2, "category_id": 4, "uploaded_timestamp": 2},
              {"file_id": 3, "category_id": 1, "uploaded_timestamp": 3},
              {"file_id": 4, "category_id": 3, "uploaded_timestamp": 4}
            ],
            "file_updates": [
              {"old_file_id": 1, "new_file_id": 2, "uploaded_timestamp": 2},
              {"old_file_id": 2, "new_file_id": 3, "uploaded_timestamp": 3},
              {"old_file_id": 4, "new_file_id": 4, "uploaded_timestamp": 4},
              {"old_file_id": 0, "new_file_id": 3, "uploaded_timestamp": 3}
            ]}
            """));

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(Smapi, Cancellation);

        Assert.Equal(["2", "3", null, null], files.Select(file => file.ReplacedBy?.Id));
    }

    [Fact]
    public async Task AnAnswerWithoutUpdateChainsReplacesNothing()
    {
        _api.Answer(FilesPath, response => response.WithStatusCode(200).WithBody("""
            {"files": [{"file_id": 1, "category_id": 1, "uploaded_timestamp": 1}]}
            """));

        Assert.Null(Assert.Single(await Source.GetFilesAsync(Smapi, Cancellation)).ReplacedBy);
    }

    [Fact]
    public async Task AModWithoutAChangelogHasNone()
    {
        _api.Serve(RecordedNexusApi.Premium, "changelogs");

        Assert.Empty(await Source.GetChangelogsAsync(Smapi, Cancellation));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task AnEmptyListMeansNoChangelogEither(string answer)
    {
        _api.Answer(ChangelogsPath, response => response.WithStatusCode(200).WithBody(answer));

        Assert.Empty(await Source.GetChangelogsAsync(Smapi, Cancellation));
    }

    [Fact]
    public async Task KeepsEachVersionsChangesInTheOrderGiven()
    {
        _api.Answer(ChangelogsPath, response => response.WithStatusCode(200).WithBody("""
            {"4.5.1": ["Fixed the crash on start.", "Added a setting."], "4.5.2": ["Fixed the setting.", 7], "4.6": "not a list"}
            """));

        IReadOnlyList<ModChangelog> changelogs = await Source.GetChangelogsAsync(Smapi, Cancellation);

        Assert.Equal(["4.5.1", "4.5.2", "4.6"], changelogs.Select(changelog => changelog.Version));
        Assert.Equal(["Fixed the crash on start.", "Added a setting."], changelogs[0].Changes);
        Assert.Equal(["Fixed the setting."], changelogs[1].Changes);
        Assert.Empty(changelogs[2].Changes);
    }

    [Theory]
    [InlineData("sky/rim", "2400")]
    [InlineData("stardewvalley", "SMAPI")]
    [InlineData("stardewvalley", "0")]
    public async Task RefusesAModWithIdsNexusModsDoesntGiveWithoutAskingTheApi(string game, string mod)
    {
        ModRef odd = new(new GameRef(new SourceId("nexus"), game), mod);

        await Assert.ThrowsAsync<ArgumentException>(() => Source.GetModAsync(odd, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.GetFilesAsync(odd, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.GetChangelogsAsync(odd, Cancellation));
        Assert.Empty(_api.Requests);
    }

    /// <summary>
    /// The ID of the file at the end of <paramref name="file"/>'s update chain.
    /// </summary>
    private static string NewestOf(ModFile file, Dictionary<string, ModFile> byId)
    {
        ModFile newest = file;
        HashSet<string> seen = [];
        while (newest.ReplacedBy is { } newer && seen.Add(newest.Reference.Id) && byId.TryGetValue(newer.Id, out ModFile? next))
        {
            newest = next;
        }

        return newest.Reference.Id;
    }

    /// <summary>
    /// The recorded game, as one entry of a list of games written by hand: the recorded list's entries have the same
    /// shape, but for the game's categories.
    /// </summary>
    private static JsonNode RecordedGame() => RecordedNexusApi.Recording(RecordedNexusApi.Premium, "game")["response"]!["body"]!.DeepClone();

    private void AnswerGames(params JsonNode?[] games)
    {
        string body = new JsonArray([.. games.Select(game => game?.DeepClone())]).ToJsonString();
        _api.Answer("/v1/games.json", response => response.WithStatusCode(HttpStatusCode.OK).WithBody(body));
    }
}
