using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Nexus.Tests.Common;

using WireMock.ResponseBuilders;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Searching a game's mods through the v2 GraphQL API. The answers here are written by hand from the shape other
/// mod managers use; the recording run replaces them with real ones.
/// </summary>
public sealed class NexusSourceSearchTests : IDisposable
{
    private const string GraphQlPath = "/v2/graphql";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceSearchTests()
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
    public async Task AsksForTheGamesModsWhoseNamesMatchTheWords()
    {
        _api.AnswerPost(GraphQlPath, Page(total: 0));

        await Source.SearchAsync(StardewValley, new SearchQuery("smapi"), Cancellation);

        JsonNode body = SentBody();
        Assert.StartsWith("query SearchMods(", Query(body), StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {
                  "filter": {
                    "gameDomainName": [{"value": "stardewvalley", "op": "EQUALS"}],
                    "nameStemmed": [{"value": "smapi", "op": "MATCHES"}],
                    "adultContent": [{"value": false, "op": "EQUALS"}]
                  },
                  "sort": [{"relevance": {"direction": "DESC"}}],
                  "offset": 0,
                  "count": 20
                }
                """),
            body["variables"]),
            body["variables"]!.ToJsonString());
    }

    [Fact]
    public async Task FiltersByAuthorAndIncludesAdultContentWhenAsked()
    {
        _api.AnswerPost(GraphQlPath, Page(total: 0));

        await Source.SearchAsync(StardewValley, new SearchQuery(author: "Pathoschild", includeAdultContent: true, offset: 40, count: 10), Cancellation);

        JsonNode variables = SentBody()["variables"]!;
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {
                  "gameDomainName": [{"value": "stardewvalley", "op": "EQUALS"}],
                  "author": [{"value": "Pathoschild", "op": "MATCHES"}]
                }
                """),
            variables["filter"]),
            variables["filter"]!.ToJsonString());
        Assert.Equal((40, 10), (variables["offset"]!.GetValue<int>(), variables["count"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData("smapi", SearchSort.Relevance, "relevance")]
    [InlineData(null, SearchSort.Relevance, "endorsements")]
    [InlineData("smapi", SearchSort.Endorsements, "endorsements")]
    [InlineData("smapi", SearchSort.Downloads, "downloads")]
    [InlineData(null, SearchSort.Updated, "updatedAt")]
    [InlineData(null, SearchSort.Newest, "createdAt")]
    public async Task SortsTheWayAsked(string? text, SearchSort sort, string field)
    {
        _api.AnswerPost(GraphQlPath, Page(total: 0));

        await Source.SearchAsync(StardewValley, new SearchQuery(text, sort), Cancellation);

        JsonObject order = Assert.Single(SentBody()["variables"]!["sort"]!.AsArray())!.AsObject();
        Assert.Equal(field, Assert.Single(order).Key);
        Assert.Equal("DESC", order[field]!["direction"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReadsTheModsOnThePage()
    {
        _api.AnswerPost(GraphQlPath, Page(total: 45, """
            {
              "modId": 2400, "name": "SMAPI - Stardew Modding API", "summary": "The mod loader.", "author": "Pathoschild",
              "version": "4.5.2", "pictureUrl": "https://staticdelivery.nexusmods.com/mods/1303/images/2400/picture.png",
              "updatedAt": "2026-03-14T19:07:23Z", "endorsements": 387171, "downloads": 30286114, "adultContent": false,
              "status": "published", "game": {"domainName": "stardewvalley"}
            }
            """, """{"modId": 1915, "name": "Content Patcher", "game": {"domainName": "stardewvalley"}}"""));

        SearchPage page = await Source.SearchAsync(StardewValley, new SearchQuery("smapi", offset: 20, count: 2), Cancellation);

        Assert.Equal((45, 20, true), (page.TotalCount, page.Offset, page.HasMore));
        Assert.Equal(["2400", "1915"], page.Mods.Select(mod => mod.Mod.Id));
        ModInfo smapi = page.Mods[0];
        Assert.Same(StardewValley, smapi.Mod.Game);
        Assert.Equal(
            ("SMAPI - Stardew Modding API", "The mod loader.", "Pathoschild", "4.5.2"),
            (smapi.Name, smapi.Summary, smapi.Author, smapi.Version));
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), smapi.PageUrl);
        Assert.Equal(new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/2400/picture.png"), smapi.PictureUrl);
        Assert.Equal(new DateTimeOffset(2026, 3, 14, 19, 7, 23, TimeSpan.Zero), smapi.UpdatedAt);
        Assert.Equal((30286114L, 387171L), (smapi.Downloads!.Value, smapi.Endorsements!.Value));
        Assert.True(smapi.IsAvailable);
        Assert.False(smapi.HasAdultContent);
    }

    [Fact]
    public async Task FillsInWhatAModLeavesOut()
    {
        _api.AnswerPost(GraphQlPath, Page(
            total: 1,
            """{"modId": 7, "pictureUrl": "javascript:alert(1)", "status": "hidden", "adultContent": true, "downloads": -1}"""));

        ModInfo mod = Assert.Single((await Source.SearchAsync(StardewValley, new SearchQuery(), Cancellation)).Mods);

        Assert.Equal("Mod 7", mod.Name);
        Assert.Null(mod.PictureUrl);
        Assert.Null(mod.Downloads);
        Assert.False(mod.IsAvailable);
        Assert.True(mod.HasAdultContent);
    }

    [Fact]
    public async Task LeavesOutAnotherGamesMods()
    {
        _api.AnswerPost(GraphQlPath, Page(
            total: 2,
            """{"modId": 1, "name": "Here", "game": {"domainName": "StardewValley"}}""",
            """{"modId": 2, "name": "Elsewhere", "game": {"domainName": "skyrimspecialedition"}}"""));

        SearchPage page = await Source.SearchAsync(StardewValley, new SearchQuery(), Cancellation);

        Assert.Equal("Here", Assert.Single(page.Mods).Name);
    }

    [Fact]
    public async Task AnEmptyPageHasNoMore()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody("""{"data": {"mods": {"totalCount": 0, "nodes": null}}}"""));

        SearchPage page = await Source.SearchAsync(StardewValley, new SearchQuery("nothing like it"), Cancellation);

        Assert.Empty(page.Mods);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ARefusedSearchSaysWhy()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            """{"data": null, "errors": [{"message": "Field 'nameStemmed' is not defined by type 'ModsFilter'."}]}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.SearchAsync(StardewValley, new SearchQuery("smapi"), Cancellation));

        Assert.Contains("nameStemmed", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerWithoutResultsIsAFailure()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody("""{"data": {"mods": null}}"""));

        await Assert.ThrowsAsync<HttpRequestException>(() => Source.SearchAsync(StardewValley, new SearchQuery(), Cancellation));
    }

    [Fact]
    public async Task RefusesAGameNexusModsDoesntNameWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.SearchAsync(new GameRef(new SourceId("nexus"), "stardew/valley"), new SearchQuery(), Cancellation));

        Assert.Empty(_api.Requests);
    }

    private static Func<IResponseBuilder, IResponseBuilder> Page(int total, params string[] nodes)
        => response => response.WithStatusCode(200).WithBody(
            new JsonObject
            {
                ["data"] = new JsonObject
                {
                    ["mods"] = new JsonObject
                    {
                        ["totalCount"] = total,
                        ["nodes"] = new JsonArray([.. nodes.Select(node => JsonNode.Parse(node))]),
                    },
                },
            }.ToJsonString());

    private static string Query(JsonNode body) => body["query"]!.GetValue<string>().Split('\n').First(line => line.StartsWith("query ", StringComparison.Ordinal));

    private JsonNode SentBody() => JsonNode.Parse(Assert.Single(_api.Requests).RequestMessage!.Body!)!;
}
