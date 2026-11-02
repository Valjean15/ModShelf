using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// A mod's details from the v2 GraphQL API, with its thumbnails and category, recorded for SMAPI; and the v1 API asked
/// instead when v2 doesn't give the mod.
/// </summary>
public sealed class NexusSourceDetailsTests : IDisposable
{
    private const string GraphQlPath = "/v2/graphql";
    private const string ModPath = "/v1/games/stardewvalley/mods/2400.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");
    private static readonly ModRef Smapi = new(StardewValley, "2400");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceDetailsTests()
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
    public async Task DescribesAModFromTheNewerApiWithItsThumbnailsAndCategory()
    {
        _api.Serve(RecordedNexusApi.Premium, "details-mod");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        JsonNode recorded = RecordedNexusApi.Recording(RecordedNexusApi.Premium, "details-mod")["response"]!["body"]!["data"]!["legacyModsByDomain"]!["nodes"]![0]!;
        Assert.Same(Smapi, mod.Mod);
        Assert.Equal("SMAPI - Stardew Modding API", mod.Name);
        Assert.Equal(("Pathoschild", "4.5.2", "Modding Tools"), (mod.Author, mod.Version, mod.Category));
        Assert.Equal(new Uri(recorded["thumbnailUrl"]!.GetValue<string>()), mod.ThumbnailUrl);
        Assert.Equal(new Uri(recorded["thumbnailBlurredUrl"]!.GetValue<string>()), mod.ThumbnailBlurredUrl);
        Assert.Equal(new Uri(recorded["pictureUrl"]!.GetValue<string>()), mod.PictureUrl);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), mod.PageUrl);
        Assert.Equal((30407540L, 388540L), (mod.Downloads, mod.Endorsements));
        Assert.Equal(DateTimeOffset.Parse("2026-03-14T19:07:23Z", System.Globalization.CultureInfo.InvariantCulture), mod.UpdatedAt);
        Assert.True(mod.IsAvailable);
        Assert.False(mod.HasAdultContent);

        // One request: neither v1's mod nor its game, for the category.
        Assert.Equal([GraphQlPath], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Fact]
    public async Task TakesTheCategorysNameFromEitherField()
    {
        AnswerDetails("""{"modId": 2400, "name": "SMAPI", "category": "Modding Tools"}""");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal("Modding Tools", mod.Category);
    }

    [Fact]
    public async Task PrefersTheCategorysOwnName()
    {
        AnswerDetails("""{"modId": 2400, "name": "SMAPI", "category": "Old name", "modCategory": {"categoryId": 9, "name": "Modding Tools"}}""");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal("Modding Tools", mod.Category);
    }

    [Fact]
    public async Task AModTheNewerApiDoesntListIsReadFromTheOlderOne()
    {
        AnswerDetails();
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.Serve(RecordedNexusApi.Premium, "game");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal(("SMAPI - Stardew Modding API", "Modding Tools"), (mod.Name, mod.Category));
        Assert.Null(mod.ThumbnailUrl);
        Assert.Contains(_api.Requests, request => request.RequestMessage?.Path == ModPath);
    }

    [Theory]
    [InlineData("""{"modId": 2401, "name": "Another mod"}""")]
    [InlineData("""{"modId": 2400, "name": "Another game's mod", "game": {"domainName": "skyrimspecialedition"}}""")]
    public async Task AnotherModInTheAnswerIsntTaken(string node)
    {
        AnswerDetails(node);
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.Serve(RecordedNexusApi.Premium, "game");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal("SMAPI - Stardew Modding API", mod.Name);
    }

    [Theory]
    [InlineData(200, """{"data": null, "errors": [{"message": "Cannot query field 'modCategory' on type 'Mod'."}]}""")]
    [InlineData(200, """{"data": null}""")]
    [InlineData(200, """{"data": {"legacyModsByDomain": "not a page"}}""")]
    [InlineData(400, """{"errors": [{"message": "Bad request"}]}""")]
    public async Task AnAnswerThatIsntTheModIsAskedOfTheOlderApi(int status, string body)
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(status).WithBody(body));
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.Serve(RecordedNexusApi.Premium, "game");

        ModInfo mod = await Source.GetModAsync(Smapi, Cancellation);

        Assert.Equal("SMAPI - Stardew Modding API", mod.Name);
    }

    [Fact]
    public async Task TheRateLimitIsntSpentAgainOnTheOlderApi()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(429));
        _api.Serve(RecordedNexusApi.Premium, "mod");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetModAsync(Smapi, Cancellation));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.DoesNotContain(_api.Requests, request => request.RequestMessage?.Path == ModPath);
    }

    [Fact]
    public async Task AModNeitherApiKnowsIsNotFound()
    {
        ModRef unknown = new(StardewValley, "999999999");
        _api.Serve(RecordedNexusApi.Premium, "details-mod-not-found");
        _api.Serve(RecordedNexusApi.Premium, "mod-not-found");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetModAsync(unknown, Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>
    /// The newer API answers the details query with these nodes, whatever it's asked.
    /// </summary>
    private void AnswerDetails(params string[] nodes)
        => _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            $$$$"""{"data": {"legacyModsByDomain": {"totalCount": {{{{nodes.Length}}}}, "nodes": [{{{{string.Join(", ", nodes)}}}}]}}}"""));
}
