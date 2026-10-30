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
/// Searching a game's mods through the v2 GraphQL API: seven searches recorded from Nexus Mods, each answered only when
/// the source asks what was recorded, and answers written by hand for what the recordings don't show.
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

    [Theory]
    [InlineData("search-words", "SMAPI", SearchSort.Relevance, 0, null, false, SearchDirection.Descending, 33, "2367", "SMAPI Templates")]
    [InlineData("search-downloads", "SMAPI", SearchSort.Downloads, 0, null, false, SearchDirection.Descending, 33, "2400", "SMAPI - Stardew Modding API")]
    [InlineData("search-endorsed-page-2", null, SearchSort.Endorsements, 5, null, false, SearchDirection.Descending, 33710, "4", "CJB Cheats Menu")]
    [InlineData("search-author-updated", null, SearchSort.Updated, 0, "Pathoschild", false, SearchDirection.Descending, 42, "53956", "The Neighborly Collection - Tractor Garage")]
    [InlineData("search-newest-with-adult", null, SearchSort.Newest, 0, null, true, SearchDirection.Descending, 35063, "54840", "SoulCovenant")]
    [InlineData("search-oldest-first", null, SearchSort.Newest, 0, null, false, SearchDirection.Ascending, 33710, "3", "Rise and Shine - Configurable wake-up time")]
    public async Task SearchesAsNexusModsAnsweredThem(
        string recording,
        string? text,
        SearchSort sort,
        int offset,
        string? author,
        bool includeAdultContent,
        SearchDirection direction,
        int total,
        string firstId,
        string firstName)
    {
        _api.Serve(RecordedNexusApi.Premium, recording);

        SearchPage page = await Source.SearchAsync(
            StardewValley, new SearchQuery(text, sort, offset, count: 5, author, includeAdultContent, direction), Cancellation);

        // Answered only when asked for what was recorded; and nothing more was asked.
        JsonNode recorded = RecordedNexusApi.Recording(RecordedNexusApi.Premium, recording)["request"]!["body"]!["variables"]!;
        Assert.True(JsonNode.DeepEquals(recorded, SentBody()["variables"]), SentBody()["variables"]!.ToJsonString());
        Assert.Equal((total, offset), (page.TotalCount, page.Offset));
        Assert.Equal(5, page.Mods.Count);
        Assert.Equal((firstId, firstName), (page.Mods[0].Mod.Id, page.Mods[0].Name));
        Assert.All(page.Mods, mod => Assert.Same(StardewValley, mod.Mod.Game));
    }

    [Fact]
    public async Task ASearchNexusModsFindsNothingForHasNoMods()
    {
        _api.Serve(RecordedNexusApi.Premium, "search-no-results");

        SearchPage page = await Source.SearchAsync(StardewValley, new SearchQuery("zqxj no such mod zqxj", count: 5), Cancellation);

        Assert.Equal((0, false), (page.TotalCount, page.HasMore));
        Assert.Empty(page.Mods);
    }

    [Fact]
    public async Task ARecordedModReadsInFull()
    {
        _api.Serve(RecordedNexusApi.Premium, "search-words");

        ModInfo templates = (await Source.SearchAsync(StardewValley, new SearchQuery("SMAPI", count: 5), Cancellation)).Mods[0];

        Assert.Equal(
            ("A collection of SMAPI Templates for Visual Studio", "Platonymous", "1.4.3"),
            (templates.Summary, templates.Author, templates.Version));
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/2367"), templates.PageUrl);
        Assert.Equal(new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/2367/2367-1528675903-1038100234.jpeg"), templates.PictureUrl);
        Assert.Equal(new DateTimeOffset(2020, 3, 2, 15, 12, 3, TimeSpan.Zero), templates.UpdatedAt);
        Assert.Equal((1613L, 71L), (templates.Downloads!.Value, templates.Endorsements!.Value));
        Assert.True(templates.IsAvailable);
        Assert.False(templates.HasAdultContent);
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
    public async Task TurnsTheSortAroundWhenAsked()
    {
        _api.AnswerPost(GraphQlPath, Page(total: 0));

        await Source.SearchAsync(StardewValley, new SearchQuery(sort: SearchSort.Newest, direction: SearchDirection.Ascending), Cancellation);

        JsonObject order = Assert.Single(SentBody()["variables"]!["sort"]!.AsArray())!.AsObject();
        Assert.Equal("ASC", order["createdAt"]!["direction"]!.GetValue<string>());
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
