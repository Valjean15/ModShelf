using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Endorsements;
using ModShelf.Sources.Nexus.Tests.Common;

using WireMock.Logging;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The account's endorsements: listed, given and abstained from, and a refusal in Nexus Mods' words.
/// </summary>
/// <remarks>
/// Endorsing changes the account, so it isn't recorded: these answers are written by hand, to the shape the v1 API
/// documents and Nexus Mods' own app reads.
/// </remarks>
public sealed class NexusSourceEndorsementTests : IDisposable
{
    private const string EndorsementsPath = "/v1/user/endorsements.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");
    private static readonly ModRef Smapi = new(StardewValley, "2400");
    private static readonly ModRef ContentPatcher = new(StardewValley, "1915");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceEndorsementTests()
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
    public async Task ListsWhatTheAccountSaidOfEachModItsLastWordKept()
    {
        _api.Answer(EndorsementsPath, response => response.WithStatusCode(200).WithBody(
            """
            [
              {"mod_id": 2400, "domain_name": "stardewvalley", "date": 1761566400, "version": "4.5.2", "status": "Endorsed"},
              {"mod_id": 1915, "domain_name": "StardewValley", "date": 1761566400, "version": "2.0", "status": "Abstained"},
              {"mod_id": 12604, "domain_name": "skyrimspecialedition", "date": 1761566400, "version": "5.2", "status": "Undecided"},
              {"mod_id": 0, "domain_name": "stardewvalley", "status": "Endorsed"},
              {"mod_id": 5, "domain_name": "../users", "status": "Endorsed"},
              {"mod_id": 1915, "domain_name": "stardewvalley", "status": "Endorsed"},
              null
            ]
            """));

        IReadOnlyDictionary<ModRef, Endorsement> said = await Source.GetEndorsementsAsync(Cancellation);

        Assert.Equal(2, said.Count);
        Assert.Equal((Endorsement.Endorsed, Endorsement.Endorsed), (said[Smapi], said[ContentPatcher]));
    }

    [Fact]
    public async Task EndorsesWithTheVersionGiven()
    {
        _api.AnswerPost("/v1/games/stardewvalley/mods/2400/endorse.json", response => response.WithStatusCode(200).WithBody(
            """{"message": "Updated endorsement status", "status": "Endorsed"}"""));

        await Source.EndorseAsync(Smapi, Endorsement.Endorsed, " 4.5.2 ", Cancellation);

        ILogEntry sent = Assert.Single(_api.Requests);
        Assert.Equal(("POST", "version=4.5.2"), (sent.RequestMessage?.Method, sent.RequestMessage?.Body));
    }

    [Fact]
    public async Task AbstainsWithTheModsOwnVersionWhenNoneIsGiven()
    {
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.AnswerPost("/v1/games/stardewvalley/mods/2400/abstain.json", response => response.WithStatusCode(200).WithBody(
            """{"message": "Updated endorsement status", "status": "Abstained"}"""));

        await Source.EndorseAsync(Smapi, Endorsement.Abstained, null, Cancellation);

        Assert.Equal(
            ["/v1/games/stardewvalley/mods/2400.json", "/v1/games/stardewvalley/mods/2400/abstain.json"],
            _api.Requests.Select(request => request.RequestMessage?.Path));
        Assert.Equal("version=4.5.2", _api.Requests[^1].RequestMessage?.Body);
    }

    [Fact]
    public async Task ARefusalSaysWhatNexusModsSaid()
    {
        _api.AnswerPost("/v1/games/stardewvalley/mods/2400/endorse.json", response => response.WithStatusCode(403).WithBody(
            """{"code": 403, "message": "NOT_DOWNLOADED_MOD"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.EndorseAsync(Smapi, Endorsement.Endorsed, "4.5.2", Cancellation));

        Assert.Contains("NOT_DOWNLOADED_MOD", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnotherSourcesModWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.EndorseAsync(new ModRef(new GameRef(new SourceId("other"), "stardewvalley"), "2400"), Endorsement.Endorsed, "1", Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public void NamesEndorsingInItsOwnWords()
    {
        Assert.Equal(("endorse", "abstain", true), (Source.Words.Approve, Source.Words.Unapprove, Source.Words.CanApprove));
    }
}
