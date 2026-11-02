using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// What a mod needs, from the v2 GraphQL API: Content Patcher, which needs SMAPI, and SMAPI, which needs nothing, as
/// recorded; and answers written by hand for what the recordings don't show, such as things elsewhere.
/// </summary>
public sealed class NexusSourceRequirementsTests : IDisposable
{
    private const string GraphQlPath = "/v2/graphql";
    private const string GamePath = "/v1/games/stardewvalley.json";
    private const string GamesPath = "/v1/games.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");
    private static readonly ModRef Smapi = new(StardewValley, "2400");
    private static readonly ModRef ContentPatcher = new(StardewValley, "1915");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceRequirementsTests()
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
    public async Task ListsTheModsAModNeedsAsNexusModsAnswered()
    {
        _api.Serve(RecordedNexusApi.Premium, "requirements-requiring-mod");
        _api.Serve(RecordedNexusApi.Premium, "game");

        IReadOnlyList<ModRequirement> needs = await Source.GetRequirementsAsync(ContentPatcher, Cancellation);

        // SMAPI, in game 1303, which the recorded game says is Stardew Valley; its notes are empty.
        ModRequirement smapi = Assert.Single(needs);
        Assert.Equal(new ModRequirement("SMAPI - Stardew Modding API", Smapi), smapi);
        Assert.Equal([GraphQlPath, GamePath], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Fact]
    public async Task AModThatNeedsNothingHasNoRequirements()
    {
        _api.Serve(RecordedNexusApi.Premium, "requirements-mod");

        IReadOnlyList<ModRequirement> needs = await Source.GetRequirementsAsync(Smapi, Cancellation);

        Assert.Empty(needs);
        Assert.Equal([GraphQlPath], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Fact]
    public async Task TheGameReadWithTheModsPageNamesItsRequirementsGame()
    {
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "requirements-requiring-mod");

        await Source.GetGameAsync(StardewValley, Cancellation);
        IReadOnlyList<ModRequirement> needs = await Source.GetRequirementsAsync(ContentPatcher, Cancellation);

        Assert.Equal(Smapi, Assert.Single(needs).Mod);
        Assert.Single(_api.Requests, request => request.RequestMessage?.Path == GamePath);
    }

    [Fact]
    public async Task AnotherGamesModIsNamedFromTheListOfGames()
    {
        AnswerRequirements(Need(modId: "1", name: "A Morrowind mod", gameId: "100", notes: "Its main file."), Need(modId: "2", name: "Another", gameId: "100"));
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "games");

        await Source.GetGameAsync(StardewValley, Cancellation);
        IReadOnlyList<ModRequirement> needs = await Source.GetRequirementsAsync(ContentPatcher, Cancellation);

        // Game 100 is Morrowind in the recorded list, read once; Stardew Valley, read with the page, isn't read again.
        GameRef morrowind = new(new SourceId("nexus"), "morrowind");
        Assert.Equal(
            [new ModRequirement("A Morrowind mod", new ModRef(morrowind, "1"), notes: "Its main file."), new ModRequirement("Another", new ModRef(morrowind, "2"))],
            needs);
        Assert.Single(_api.Requests, request => request.RequestMessage?.Path == GamesPath);
        Assert.Single(_api.Requests, request => request.RequestMessage?.Path == GamePath);
    }

    [Fact]
    public async Task ARequirementThatDoesntSayWhereItIsIsAModOfNexusMods()
    {
        AnswerRequirements("""{"modId": "2400", "modName": "SMAPI", "gameId": "1303"}""");
        _api.Serve(RecordedNexusApi.Premium, "game");

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal(Smapi, need.Mod);
    }

    [Fact]
    public async Task AModOfAGameNoneNamesIsListedWithoutAWayToOpenIt()
    {
        AnswerRequirements(Need(modId: "7", name: "Somewhere", gameId: "424242"), Need(modId: "8", name: "Elsewhere", gameId: "424242"));
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "games");

        IReadOnlyList<ModRequirement> needs = await Source.GetRequirementsAsync(ContentPatcher, Cancellation);

        Assert.Equal([("Somewhere", null, null), ("Elsewhere", null, null)], needs.Select(need => (need.Name, need.Mod, need.PageUrl)));

        // The list of games, read once, doesn't name it; it isn't read again for the next.
        Assert.Single(_api.Requests, request => request.RequestMessage?.Path == GamesPath);
    }

    [Fact]
    public async Task AGameThatCantBeReadIsReadAgainWithTheNextMod()
    {
        _api.Serve(RecordedNexusApi.Premium, "requirements-requiring-mod");
        _api.AnswerInTurn(
            GamePath,
            // Not one the client retries.
            response => response.WithStatusCode(404),
            response => response.WithStatusCode(200).WithBody(RecordedNexusApi.Recording(RecordedNexusApi.Premium, "game")["response"]!["body"]!.ToJsonString()));
        _api.Answer(GamesPath, response => response.WithStatusCode(404));

        ModRequirement unnamed = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));
        ModRequirement named = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Null(unnamed.Mod);
        Assert.Equal(Smapi, named.Mod);
    }

    [Fact]
    public async Task SomethingElsewhereHasItsPageAndNotes()
    {
        AnswerRequirements("""{"modId": "", "modName": "Script Extender", "gameId": "1303", "notes": "For every file.", "url": "https://skse.silverlock.org/", "externalRequirement": true}""");

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal(new ModRequirement("Script Extender", pageUrl: new Uri("https://skse.silverlock.org/"), notes: "For every file."), need);
        Assert.Equal([GraphQlPath], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("skse.silverlock.org")]
    [InlineData("javascript:alert(1)")]
    public async Task SomethingElsewhereWithoutAWebLinkHasNoPage(string url)
    {
        AnswerRequirements($$"""{"modName": "Script Extender", "notes": "", "url": "{{url}}", "externalRequirement": true}""");

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal(new ModRequirement("Script Extender"), need);
    }

    [Fact]
    public async Task SomethingElsewhereWithoutANameIsCalledByItsSite()
    {
        AnswerRequirements("""{"modName": "", "url": "https://skse.silverlock.org/", "externalRequirement": true}""");

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal("skse.silverlock.org", need.Name);
    }

    [Fact]
    public async Task AModWithoutANameIsCalledByItsId()
    {
        AnswerRequirements(Need(modId: "2400", name: "", gameId: "1303"));
        _api.Serve(RecordedNexusApi.Premium, "game");

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal(new ModRequirement("Mod 2400", Smapi), need);
    }

    [Fact]
    public async Task RequirementsWithNothingToCallThemByAreLeftOut()
    {
        AnswerRequirements(
            """{"modName": "", "url": "", "externalRequirement": true}""",
            Need(modId: "not a number", name: "", gameId: "1303"),
            "null",
            Need(modId: "not a number", name: "Still named", gameId: "1303"));

        ModRequirement need = Assert.Single(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Equal(new ModRequirement("Still named"), need);
    }

    [Theory]
    [InlineData("""{"data": {"legacyModsByDomain": {"nodes": []}}}""")]
    [InlineData("""{"data": {"legacyModsByDomain": {"nodes": [{"modId": 1916, "modRequirements": {"nexusRequirements": {"totalCount": 1, "nodes": [{"modId": "2400", "modName": "SMAPI", "gameId": "1303"}]}}}]}}}""")]
    [InlineData("""{"data": {"legacyModsByDomain": {"nodes": [{"modId": 1915, "modRequirements": null}]}}}""")]
    public async Task AModNexusModsDoesntListNeedsNothing(string answer)
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(answer));

        Assert.Empty(await Source.GetRequirementsAsync(ContentPatcher, Cancellation));
    }

    [Fact]
    public async Task ARefusedQueryIsAFailure()
    {
        _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            """{"data": null, "errors": [{"message": "Cannot query field 'nexusRequirements' on type 'ModRequirements'."}]}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetRequirementsAsync(ContentPatcher, Cancellation));

        Assert.Contains("nexusRequirements", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnotherSourcesModWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.GetRequirementsAsync(new ModRef(new GameRef(new SourceId("other"), "stardewvalley"), "1915"), Cancellation));

        Assert.Empty(_api.Requests);
    }

    private static string Need(string modId, string name, string gameId, string notes = "")
        => $$"""{"id": "[1, {{gameId}}]", "modId": "{{modId}}", "modName": "{{name}}", "gameId": "{{gameId}}", "notes": "{{notes}}", "url": "", "externalRequirement": false}""";

    /// <summary>
    /// The newer API answers the requirements query, for Content Patcher, with these requirements.
    /// </summary>
    private void AnswerRequirements(params string[] needs)
        => _api.AnswerPost(GraphQlPath, response => response.WithStatusCode(200).WithBody(
            $$$$"""{"data": {"legacyModsByDomain": {"nodes": [{"modId": 1915, "game": {"domainName": "stardewvalley"}, "modRequirements": {"nexusRequirements": {"totalCount": {{{{needs.Length}}}}, "nodes": [{{{{string.Join(", ", needs)}}}}]}}}]}}}"""));
}
