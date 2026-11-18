using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Collections;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// A collection's files, from the v2 GraphQL API: the revision a link names, or the latest, with each file once, those
/// Nexus Mods no longer offers counted, and what the collection needs from elsewhere.
/// </summary>
/// <remarks>
/// No collection is recorded yet, so these answers are written by hand, to the shape of the schema's
/// <c>CollectionRevision</c>; the recording script records one when the owner runs it.
/// </remarks>
public sealed class NexusSourceCollectionTests : IDisposable
{
    private const string GraphQlPath = "/v2/graphql";

    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");
    private static readonly ModRef SkyUi = new(Skyrim, "12604");
    private static readonly ModRef Address = new(Skyrim, "32444");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceCollectionTests()
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
    public async Task ReadsTheLatestRevisionsFilesAndWhatItNeedsElsewhere()
    {
        AnswerRevision(
            """
            {
              "revisionNumber": 12,
              "collection": {"name": "Essentials ", "slug": "qdurkx"},
              "modFiles": [
                {"fileId": 35407, "optional": false, "version": "5.2SE", "file": {"fileId": 35407, "modId": 12604, "name": "SkyUI_5_2_SE", "version": "5.2", "category": "MAIN", "game": {"domainName": "skyrimspecialedition"}, "mod": {"name": "SkyUI", "author": "SkyUI Team"}}},
                {"fileId": 35408, "optional": true, "version": "", "file": {"fileId": 35408, "modId": 12604, "name": "SkyUI Extras", "version": "1.1", "category": "OPTIONAL", "game": {"domainName": "skyrimspecialedition"}, "mod": {"name": "SkyUI", "author": ""}}},
                {"fileId": 99, "optional": false, "version": "1.0", "file": null},
                {"fileId": 501, "optional": false, "version": "11", "file": {"fileId": 501, "modId": 32444, "name": "Address Library", "version": "11", "category": "old_version", "game": null, "mod": null}},
                {"fileId": 501, "optional": false, "version": "11", "file": {"fileId": 501, "modId": 32444, "name": "Address Library", "version": "11", "category": "OLD_VERSION", "game": null, "mod": null}},
                null
              ],
              "externalResources": [
                {"name": "SKSE64", "optional": false, "resourceType": "browse", "resourceUrl": "https://skse.silverlock.org/"},
                {"name": "A bundled patch", "optional": true, "resourceType": "bundle", "resourceUrl": ""},
                {"name": " ", "optional": false, "resourceType": "browse", "resourceUrl": "https://example.com/"},
                null
              ]
            }
            """);

        CollectionRevision? read = await Source.GetCollectionAsync(new Uri("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx"), Cancellation);

        Assert.NotNull(read);
        Assert.Equal(
            ("Essentials", Skyrim, "https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", 12, 1),
            (read.Name, read.Game, read.PageUrl.AbsoluteUri, read.Revision, read.FilesUnavailable));
        Assert.Equal(
            [
                new CollectionFile(new ModFileRef(SkyUi, "35407"), "SkyUI", "SkyUI_5_2_SE", "5.2SE", ModFileCategory.Main, optional: false, "SkyUI Team"),
                new CollectionFile(new ModFileRef(SkyUi, "35408"), "SkyUI", "SkyUI Extras", "1.1", ModFileCategory.Optional, optional: true),
                new CollectionFile(new ModFileRef(Address, "501"), "Mod 32444", "Address Library", "11", ModFileCategory.OldVersion),
            ],
            read.Files);
        Assert.Equal(
            [new CollectionResource("SKSE64", new Uri("https://skse.silverlock.org/")), new CollectionResource("A bundled patch", optional: true)],
            read.Elsewhere);
        Assert.Equal(
            new JsonObject { ["slug"] = "qdurkx", ["domainName"] = "skyrimspecialedition", ["revision"] = null }.ToJsonString(),
            SentVariables());
    }

    [Fact]
    public async Task AsksForTheRevisionTheLinkNames()
    {
        AnswerRevision("""{"revisionNumber": null, "collection": null, "modFiles": [], "externalResources": null}""");

        CollectionRevision? read = await Source.GetCollectionAsync(new Uri("nxm://skyrimspecialedition/collections/qdurkx/revisions/3"), Cancellation);

        Assert.Equal(("qdurkx", 3, 0), (read?.Name, read?.Revision, read?.Files.Count));
        Assert.Equal(
            new JsonObject { ["slug"] = "qdurkx", ["domainName"] = "skyrimspecialedition", ["revision"] = 3 }.ToJsonString(),
            SentVariables());
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407?key=a&expires=1&user_id=1")]
    [InlineData("https://example.com/collections/qdurkx")]
    public async Task ALinkThatIsntToACollectionAsksNothing(string link)
    {
        Assert.Null(await Source.GetCollectionAsync(new Uri(link), Cancellation));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task RefusesACollectionLinkItCantReadWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.GetCollectionAsync(new Uri("https://www.nexusmods.com/games/skyrimspecialedition/collections/q-x"), Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task ACollectionNexusModsDoesntHaveIsNotFound()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody("""{"data": {"collectionRevision": null}}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.GetCollectionAsync(new Uri("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task ARefusedQuerySaysWhy()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            """{"data": null, "errors": [{"message": "Collection not found"}]}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.GetCollectionAsync(new Uri("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx"), Cancellation));

        Assert.Contains("Collection not found", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The API answers the collection query with this revision.
    /// </summary>
    private void AnswerRevision(string revision)
        => _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody($$$"""{"data": {"collectionRevision": {{{revision}}}}}"""));

    /// <summary>
    /// The variables the one request sent, as JSON.
    /// </summary>
    private string SentVariables()
    {
        string body = Assert.Single(_api.Requests).RequestMessage!.Body!;
        return JsonNode.Parse(body)!["variables"]!.ToJsonString();
    }
}
