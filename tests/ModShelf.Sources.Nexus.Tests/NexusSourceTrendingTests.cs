using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// A game's trending mods, from the v1 API's list: as recorded for Stardew Valley, and answers written by hand for what
/// the recording doesn't show.
/// </summary>
public sealed class NexusSourceTrendingTests : IDisposable
{
    private const string TrendingPath = "/v1/games/stardewvalley/mods/trending.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceTrendingTests()
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
    public async Task ListsTheTrendingModsAsNexusModsAnsweredThem()
    {
        _api.Serve(RecordedNexusApi.Premium, "trending");

        IReadOnlyList<ModInfo> trending = await Source.GetTrendingAsync(StardewValley, Cancellation);

        Assert.Equal(["1915", "5098", "2400", "239", "3231"], trending.Select(mod => mod.Mod.Id));
        Assert.All(trending, mod => Assert.Same(StardewValley, mod.Mod.Game));
        ModInfo patcher = trending[0];
        Assert.Equal(
            ("Content Patcher", "Pathoschild", "2.9.1"),
            (patcher.Name, patcher.Author, patcher.Version));
        Assert.Equal("Loads content packs that change the game's data, images, and maps without replacing XNB files.", patcher.Summary);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/1915"), patcher.PageUrl);
        Assert.Equal(new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/1915/1915-1519967846-411398257.png"), patcher.PictureUrl);
        Assert.Equal(
            (DateTimeOffset.FromUnixTimeSeconds(1776338322), DateTimeOffset.FromUnixTimeSeconds(1519600998)),
            (patcher.UpdatedAt!.Value, patcher.PublishedAt!.Value));
        Assert.Equal((24951065L, 495108L), (patcher.Downloads!.Value, patcher.Endorsements!.Value));
        Assert.True(patcher.IsAvailable);
        Assert.False(patcher.HasAdultContent);

        // The v1 API gives no thumbnails, nor a size; the category's name needs the game's categories, not read yet.
        Assert.Equal((null, null), (patcher.ThumbnailUrl, patcher.ThumbnailBlurredUrl));
        Assert.Equal(((long?)null, (string?)null), (patcher.Size, patcher.Category));
        Assert.Equal([TrendingPath], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Fact]
    public async Task NamesTheCategoriesOnceTheGamesWereRead()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "trending");

        await Source.GetGameAsync(StardewValley, Cancellation);
        IReadOnlyList<ModInfo> trending = await Source.GetTrendingAsync(StardewValley, Cancellation);

        Assert.Equal(["Modding Tools", "Modding Tools", "Modding Tools", "User Interface", "Modding Tools"], trending.Select(mod => mod.Category));
    }

    [Fact]
    public async Task LeavesOutAnotherGamesModsRepeatsAndModsWithoutAnId()
    {
        _api.Answer(TrendingPath, response => response.WithStatusCode(200).WithBody(
            """
            [
              {"mod_id": 7, "name": "Seven", "domain_name": "stardewvalley", "contains_adult_content": true, "status": "hidden", "available": false},
              {"mod_id": 8, "name": "Elsewhere", "domain_name": "skyrimspecialedition"},
              {"mod_id": 7, "name": "Seven again", "domain_name": "stardewvalley"},
              {"mod_id": 0, "name": "No ID"},
              null,
              {"mod_id": 9, "name": " ", "picture_url": "javascript:alert(1)", "created_timestamp": -5}
            ]
            """));

        IReadOnlyList<ModInfo> trending = await Source.GetTrendingAsync(StardewValley, Cancellation);

        Assert.Equal([("7", "Seven"), ("9", "Mod 9")], trending.Select(mod => (mod.Mod.Id, mod.Name)));
        Assert.Equal((true, false), (trending[0].HasAdultContent, trending[0].IsAvailable));
        Assert.Null(trending[1].PictureUrl);
        Assert.Equal(DateTimeOffset.UnixEpoch, trending[1].PublishedAt);
    }

    [Fact]
    public async Task ARefusedListSaysWhy()
    {
        _api.Answer(TrendingPath, response => response.WithStatusCode(503));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetTrendingAsync(StardewValley, Cancellation));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
    }

    [Fact]
    public async Task RefusesAGameNexusModsDoesntNameWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.GetTrendingAsync(new GameRef(new SourceId("nexus"), "not a game"), Cancellation));

        Assert.Empty(_api.Requests);
    }
}
