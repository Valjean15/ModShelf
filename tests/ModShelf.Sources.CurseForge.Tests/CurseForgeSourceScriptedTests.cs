using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.CurseForge.Tests.Common;

using static ModShelf.Sources.CurseForge.Tests.Common.TestCurseForge;

namespace ModShelf.Sources.CurseForge.Tests;

/// <summary>
/// The CurseForge source against answers the tests write: what it asks CurseForge, and what it makes of the answers
/// (ADR 0107).
/// </summary>
public sealed class CurseForgeSourceScriptedTests : IDisposable
{
    private static readonly SourceId CurseForge = new("curseforge");
    private static readonly GameRef MinecraftGame = new(CurseForge, "minecraft");
    private static readonly ModRef ModMenu = new(MinecraftGame, "308702");
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CurseForgeServer _server = new();
    private ServiceProvider? _services;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private CurseForgeSource Source => CurseForgeServer.SourceOf(_services ??= _server.BuildServices());

    public void Dispose()
    {
        _services?.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task ReadsEveryPageOfGamesOnceARunAndAGamesClassesAsItsCategories()
    {
        ServeGames(51);
        _server.Answer("GET", "/v1/categories", HttpStatusCode.OK, Page(0, 2, Class(Mods, "Mods", "mc-mods"), Class(12, "Resource Packs", "texture-packs")),
            ("gameId", "432"), ("classesOnly", "true"));

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);
        GameInfo minecraft = await Source.GetGameAsync(MinecraftGame, Cancellation);
        await Source.GetGameAsync(MinecraftGame, Cancellation);

        Assert.Equal(52, games.Count);
        Assert.Equal(new GameInfo(
            MinecraftGame,
            "Minecraft",
            new Uri("https://www.curseforge.com/minecraft"),
            ["Mods", "Resource Packs"],
            new Uri("https://media.forgecdn.net/minecraft-cover.jpg"),
            new Uri("https://media.forgecdn.net/minecraft-icon.png")), minecraft);
        Assert.Equal(
            ["GET /v1/games?index=0&pageSize=50", "GET /v1/games?index=50&pageSize=50", "GET /v1/categories?gameId=432&classesOnly=true"],
            _server.Asked);
        Assert.False(Source.CouldBeGameKey("minecraft"));
    }

    [Fact]
    public async Task DescribesAModOfTheGameAndRefusesOneOfAnother()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/categories", HttpStatusCode.OK, Page(0, 1, Class(Mods, "Mods", "mc-mods")), ("gameId", "432"), ("classesOnly", "true"));
        _server.Answer("GET", "/v1/mods/308702", HttpStatusCode.OK, Data(Mod(308702, "Mod Menu", "modmenu", 5432112)));
        _server.Answer("GET", "/v1/mods/1", HttpStatusCode.OK, Data(Mod(1, "Elsewhere", "elsewhere", 2, game: 78062)));

        ModInfo mod = await Source.GetModAsync(ModMenu, Cancellation);
        HttpRequestException elsewhere = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetModAsync(new ModRef(MinecraftGame, "1"), Cancellation));

        Assert.Equal(
            ("Mod Menu", "Mod Menu, for tests.", "Prospector", new Uri(ModPage), 1234L, "Mods"),
            (mod.Name, mod.Summary, mod.Author, mod.PageUrl, mod.Downloads, mod.Category));
        Assert.Equal((new Uri("https://media.forgecdn.net/modmenu.png"), new Uri("https://media.forgecdn.net/modmenu-thumb.png")), (mod.PictureUrl, mod.ThumbnailUrl));
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        await Assert.ThrowsAsync<ArgumentException>(() => Source.GetModAsync(new ModRef(new GameRef(new SourceId("nexus"), "minecraft"), "1"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.GetModAsync(new ModRef(MinecraftGame, "modmenu"), Cancellation));
    }

    [Fact]
    public async Task ReadsAModsFilesAPageAtATimeUpToTheMostPagesItReads()
    {
        _services = _server.BuildServices(configure: options => options with { MaxFilePages = 2 });
        ServeGames(1);
        _server.Answer("GET", "/v1/mods/308702", HttpStatusCode.OK, Data(Mod(308702, "Mod Menu", "modmenu", 150)));
        for (int page = 0; page < 3; page++)
        {
            _server.Answer("GET", "/v1/mods/308702/files", HttpStatusCode.OK,
                Page(page * 50, 150, [.. Enumerable.Range(0, 50).Select(index => File(150 - (page * 50) - index, 308702, $"f{150 - (page * 50) - index}.jar", Day.AddDays(150 - (page * 50) - index)))]),
                ("index", (page * 50).ToString(System.Globalization.CultureInfo.InvariantCulture)), ("pageSize", "50"));
        }

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(ModMenu, Cancellation);

        Assert.Equal(100, files.Count);
        Assert.Equal(("150", "51"), (files[0].Reference.Id, files[^1].Reference.Id));
        Assert.Equal("150", files[1].ReplacedBy?.Id);
        Assert.DoesNotContain(_server.Asked, asked => asked.Contains("index=100", StringComparison.Ordinal));
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("CurseForge's mod 308702 has more files than ModShelf reads; the newest 100 are read", StringComparison.Ordinal));
        Assert.True(Source.FilesSideBySide);
        Assert.Empty(await Source.GetChangelogsAsync(ModMenu, Cancellation));
    }

    [Fact]
    public async Task SearchesAGamesClassByWordsInTheOrderAsked()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/categories", HttpStatusCode.OK, Page(0, 1, Class(Mods, "Mods", "mc-mods")), ("gameId", "432"), ("classesOnly", "true"));
        _server.Answer("GET", "/v1/mods/search", HttpStatusCode.OK, Page(20, 12_345, Mod(308702, "Mod Menu", "modmenu", 1), Mod(1, "Elsewhere", "elsewhere", 2, game: 78062)),
            ("gameId", "432"), ("classId", "6"), ("searchFilter", "mod menu"), ("sortField", "6"), ("sortOrder", "asc"), ("index", "20"), ("pageSize", "20"));

        SearchPage page = await Source.SearchAsync(
            MinecraftGame,
            new SearchQuery("mod menu", SearchSort.Downloads, offset: 20, count: 20, direction: SearchDirection.Ascending, category: "mods"),
            Cancellation);

        Assert.Equal([ModMenu], page.Mods.Select(mod => mod.Mod));
        Assert.Equal((10_000, 20), (page.TotalCount, page.Offset));
        Assert.Equal("GET /v1/mods/search?gameId=432&classId=6&searchFilter=mod%20menu&sortField=6&sortOrder=asc&index=20&pageSize=20", _server.Asked[^1]);
    }

    [Fact]
    public async Task RefusesASearchCurseForgeCantDo()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/categories", HttpStatusCode.OK, Page(0, 1, Class(Mods, "Mods", "mc-mods")), ("gameId", "432"), ("classesOnly", "true"));

        await Assert.ThrowsAsync<ArgumentException>(() => Source.SearchAsync(MinecraftGame, new SearchQuery(author: "Prospector"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.SearchAsync(MinecraftGame, new SearchQuery(sort: SearchSort.Size), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.SearchAsync(MinecraftGame, new SearchQuery(count: 60), Cancellation));
        ArgumentException shaders = await Assert.ThrowsAsync<ArgumentException>(() => Source.SearchAsync(MinecraftGame, new SearchQuery(category: "Shaders"), Cancellation));
        SearchPage beyond = await Source.SearchAsync(MinecraftGame, new SearchQuery(offset: 10_000), Cancellation);

        Assert.StartsWith("Minecraft has no class of mods called Shaders on CurseForge.", shaders.Message, StringComparison.Ordinal);
        Assert.Empty(beyond.Mods);
        Assert.DoesNotContain(_server.Asked, asked => asked.Contains("/v1/mods/search", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu", "5432112")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/files/5000001", "5000001")]
    [InlineData("https://curseforge.com/minecraft/mc-mods/ModMenu/download/5000002", "5000002")]
    public async Task RecognizesAModsLinkAsItsMainFileAndAFilesLinkAsThatFile(string link, string file)
    {
        ServeModBySlug();

        RecognizedLink? recognized = await Source.RecognizeAsync(new Uri(link), Cancellation);

        Assert.Equal(new ModFileRef(ModMenu, file), recognized?.File);
        Assert.Contains("GET /v1/mods/search?gameId=432&classId=6&slug=modmenu", _server.Asked);
    }

    [Fact]
    public async Task SaysWhatsWrongWithALinkToNothingOnCurseForge()
    {
        ServeModBySlug();
        _server.Answer("GET", "/v1/mods/search", HttpStatusCode.OK, Page(0, 0), ("gameId", "432"), ("classId", "6"), ("slug", "gone"));

        Assert.Null(await Source.RecognizeAsync(new Uri("https://www.nexusmods.com/minecraft/mods/1"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.RecognizeAsync(new Uri("https://www.curseforge.com/notagame/mc-mods/modmenu"), Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.RecognizeAsync(new Uri("https://www.curseforge.com/minecraft/shaders/modmenu"), Cancellation));
        HttpRequestException gone = await Assert.ThrowsAsync<HttpRequestException>(() => Source.RecognizeAsync(new Uri("https://www.curseforge.com/minecraft/mc-mods/gone"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task ResolvesAFileToItsDownloadLinkCheckedByItsSha1()
    {
        _server.Answer("GET", "/v1/mods/308702/files/5432112", HttpStatusCode.OK, Data(File(5432112, 308702, "modmenu-11.0.1.jar", Day)));

        ResolveResult result = await Source.ResolveAsync(new ModFileRef(ModMenu, "5432112"), handoff: null, Cancellation);

        DownloadTicket ticket = Assert.IsType<DownloadTicket>(result);
        Assert.Equal(new Uri("https://edge.forgecdn.net/files/5432/112/modmenu-11.0.1.jar"), ticket.Url);
        Assert.Equal(new FileHash(FileHashAlgorithm.Sha1, new string('a', 40)), ticket.ExpectedHash);
        Assert.False(Source.HandsOverLinks);
    }

    [Fact]
    public async Task AFileItsAuthorLetsNoAppDownloadIsDownloadedOnItsPage()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/mods/308702/files/5432112", HttpStatusCode.OK, Data(File(5432112, 308702, "modmenu-11.0.1.jar", Day, downloadable: false)));
        _server.Answer("GET", "/v1/mods/308702", HttpStatusCode.OK, Data(Mod(308702, "Mod Menu", "modmenu", 5432112, distributable: false)));

        ResolveResult result = await Source.ResolveAsync(new ModFileRef(ModMenu, "5432112"), handoff: null, Cancellation);

        Assert.Equal(new Uri("https://www.curseforge.com/minecraft/mc-mods/modmenu/download/5432112"), Assert.IsType<HandoffRequired>(result).PageUrl);
    }

    [Fact]
    public async Task RefusesAFileOfAnotherMod()
    {
        _server.Answer("GET", "/v1/mods/308702/files/5432112", HttpStatusCode.OK, Data(File(5432112, 1, "other.jar", Day)));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.ResolveAsync(new ModFileRef(ModMenu, "5432112"), handoff: null, Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ListsWhatTheMainFileNeedsNamedInOneRequest()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/mods/308702", HttpStatusCode.OK, Data(Mod(308702, "Mod Menu", "modmenu", 5432112)));
        _server.Answer("GET", "/v1/mods/308702/files/5432112", HttpStatusCode.OK,
            Data(File(5432112, 308702, "modmenu.jar", Day, dependencies: [(306612, 3), (999, 2), (238222, 3), (308702, 3)])));
        _server.Answer("POST", "/v1/mods", HttpStatusCode.OK, new System.Text.Json.Nodes.JsonObject
        {
            ["data"] = new System.Text.Json.Nodes.JsonArray(Mod(306612, "Fabric API", "fabric-api", 1)),
        });

        IReadOnlyList<ModRequirement> needed = await Source.GetRequirementsAsync(ModMenu, Cancellation);

        Assert.Equal(
            [
                new ModRequirement("Fabric API", new ModRef(MinecraftGame, "306612"), new Uri("https://www.curseforge.com/minecraft/mc-mods/fabric-api")),
                new ModRequirement("CurseForge mod 238222", new ModRef(MinecraftGame, "238222")),
            ],
            needed);
        Assert.Equal("{\"modIds\":[306612,238222]}", _server.Requests[^1].RequestMessage!.Body);
    }

    [Fact]
    public async Task RefusedOrMissingKeysAreTheAccountsToLinkAgain()
    {
        _services = _server.BuildServices(key: null);
        _server.RefuseWithoutKey();

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => Source.GetGamesAsync(Cancellation));

        Assert.StartsWith("CurseForge refused the API key linked, or none is linked.", refused.Message, StringComparison.Ordinal);
        Assert.False(Source.WorksWithoutAccount);
    }

    [Fact]
    public async Task LinksAKeyCurseForgeTakesNamedAfterIt()
    {
        _services = _server.BuildServices(key: null);
        _server.RefuseWithoutKey();
        _server.Answer("GET", "/v1/games", HttpStatusCode.OK, Page(0, 80, Game(Minecraft, "minecraft", "Minecraft")), ("index", "0"), ("pageSize", "1"));
        IAuthStrategy strategy = Assert.Single(Source.Strategies);
        AuthChallenge challenge = await strategy.BeginAsync(new AuthStart(), Cancellation);
        AccountCredentials credentials = await strategy.CompleteAsync(challenge, $"  {LinkedKey.Key}\n", Cancellation);

        AccountProfile profile = await Source.GetProfileAsync(credentials, AuthKind.ApiKey, Cancellation);

        Assert.Equal((AuthKind.ApiKey, new Uri("https://console.curseforge.com/#/api-keys")), (challenge.Kind, challenge.Url));
        Assert.Equal(LinkedKey.Key, credentials.AccessToken);
        Assert.Equal(("CurseForge API key", "API key"), (profile.DisplayName, profile.Tier));
        Assert.Equal(LinkedKey.Key, _server.Requests[^1].RequestMessage!.Headers!["x-api-key"].Single());
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => strategy.CompleteAsync(challenge, "04079b7a c473", Cancellation));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => strategy.CompleteAsync(challenge, "short", Cancellation));
    }

    private void ServeGames(int others)
    {
        System.Text.Json.Nodes.JsonObject[] games = [.. Enumerable.Range(1, others).Select(index => Game(index, $"game-{index}", $"Game {index}")), Game(Minecraft, "minecraft", "Minecraft")];
        for (int index = 0; index < games.Length; index += 50)
        {
            _server.Answer("GET", "/v1/games", HttpStatusCode.OK, Page(index, games.Length, [.. games.Skip(index).Take(50)]),
                ("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("pageSize", "50"));
        }
    }

    private void ServeModBySlug()
    {
        ServeGames(1);
        _server.Answer("GET", "/v1/categories", HttpStatusCode.OK, Page(0, 1, Class(Mods, "Mods", "mc-mods")), ("gameId", "432"), ("classesOnly", "true"));
        _server.Answer("GET", "/v1/mods/search", HttpStatusCode.OK, Page(0, 1, Mod(308702, "Mod Menu", "modmenu", 5432112)), ("gameId", "432"), ("classId", "6"), ("slug", "modmenu"));
    }
}
