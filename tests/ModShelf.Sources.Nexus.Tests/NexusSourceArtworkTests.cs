using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Games' pictures against the recorded API: where Nexus Mods keeps them, asked once a run, filled with each game's
/// number. Stardew Valley is game 1303.
/// </summary>
public sealed class NexusSourceArtworkTests : IDisposable
{
    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceArtworkTests()
    {
        _services = _api.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    private int GraphQlRequests => _api.Requests.Count(request => request.RequestMessage!.Path == "/v2/graphql");

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public async Task AGameComesWithItsBannerAndIcon()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "game-artwork");

        GameInfo game = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Equal(
            (new Uri("https://images.nexusmods.com/images/games/v2/1303/hero.jpg"), new Uri("https://images.nexusmods.com/images/games/v2/1303/thumbnail.jpg")),
            (game.CoverUrl, game.IconUrl));
        Assert.Equal("Stardew Valley", game.Name);
    }

    [Fact]
    public async Task EveryGameListedComesWithItsOwnPictures()
    {
        JsonNode skyrim = RecordedGame();
        skyrim["id"] = 1704;
        skyrim["domain_name"] = "skyrimspecialedition";
        skyrim["name"] = "Skyrim Special Edition";
        AnswerGames(RecordedGame(), skyrim);
        _api.Serve(RecordedNexusApi.Premium, "game-artwork");

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);

        Assert.Equal(
            [
                "https://images.nexusmods.com/images/games/v2/1303/hero.jpg https://images.nexusmods.com/images/games/v2/1303/thumbnail.jpg",
                "https://images.nexusmods.com/images/games/v2/1704/hero.jpg https://images.nexusmods.com/images/games/v2/1704/thumbnail.jpg",
            ],
            games.Select(game => $"{game.CoverUrl} {game.IconUrl}"));
    }

    [Fact]
    public async Task WhereThePicturesAreIsAskedOnceARun()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "game-artwork");
        AnswerGames(RecordedGame());

        await Task.WhenAll(Source.GetGameAsync(StardewValley, Cancellation), Source.GetGameAsync(StardewValley, Cancellation));
        await Source.GetGamesAsync(Cancellation);

        Assert.Equal(1, GraphQlRequests);
    }

    [Fact]
    public async Task ACallerStoppingDoesntStopTheQuestionForTheOthers()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        string artwork = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "game-artwork")["response"]!["body"]!.ToJsonString();
        _api.AnswerPost("/v2/graphql", response => response.WithStatusCode(HttpStatusCode.OK).WithBody(artwork).WithDelay(TimeSpan.FromSeconds(2)));
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        stopping.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source.GetGameAsync(StardewValley, stopping.Token));
        GameInfo game = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Equal(new Uri("https://images.nexusmods.com/images/games/v2/1303/hero.jpg"), game.CoverUrl);
        Assert.Equal(1, GraphQlRequests);
    }

    [Fact]
    public async Task AGameWithoutANumberHasNoPictures()
    {
        JsonNode stardew = RecordedGame();
        stardew.AsObject().Remove("id");
        JsonNode zero = RecordedGame();
        zero["id"] = 0;
        zero["domain_name"] = "zero";
        AnswerGames(stardew, zero);
        _api.Serve(RecordedNexusApi.Premium, "game-artwork");

        IReadOnlyList<GameInfo> games = await Source.GetGamesAsync(Cancellation);

        Assert.Equal(2, games.Count);
        Assert.All(games, game => Assert.Equal(((Uri?)null, (Uri?)null), (game.CoverUrl, game.IconUrl)));
    }

    [Fact]
    public async Task PicturesNexusModsDoesntPlaceLeaveGamesWithoutThemAndAreLoggedOnce()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");

        GameInfo first = await Source.GetGameAsync(StardewValley, Cancellation);
        GameInfo again = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Equal(("Stardew Valley", (Uri?)null, (Uri?)null), (first.Name, first.CoverUrl, first.IconUrl));
        Assert.Null(again.CoverUrl);
        Assert.Equal(1, GraphQlRequests);
        Assert.Single(
            _api.Logs.Entries,
            entry => entry.StartsWith(
                "[ModShelf.Sources.Nexus.NexusSource] Nexus Mods didn't say where games' pictures are, so games show without them until ModShelf starts again:",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAddressWithoutTheGamesNumberOrThatIsntAWebLinkIsntUsed()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.AnswerPost("/v2/graphql", response => response.WithStatusCode(HttpStatusCode.OK).WithBody(
            """{"data": {"gameArtwork": {"schemaV2": {"hero": "https://images.example/hero.jpg", "thumbnail": "javascript:alert({gameId})"}}}}"""));

        GameInfo game = await Source.GetGameAsync(StardewValley, Cancellation);

        Assert.Equal(((Uri?)null, (Uri?)null), (game.CoverUrl, game.IconUrl));
        Assert.DoesNotContain(_api.Logs.Entries, entry => entry.Contains("pictures", StringComparison.Ordinal));
    }

    private static JsonNode RecordedGame() => RecordedNexusApi.Recording(RecordedNexusApi.Premium, "game")["response"]!["body"]!.DeepClone();

    private void AnswerGames(params JsonNode?[] games)
    {
        string body = new JsonArray([.. games.Select(game => game?.DeepClone())]).ToJsonString();
        _api.Answer("/v1/games.json", response => response.WithStatusCode(HttpStatusCode.OK).WithBody(body));
    }
}
