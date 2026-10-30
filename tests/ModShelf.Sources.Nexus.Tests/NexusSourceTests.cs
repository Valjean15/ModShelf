using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Tests.Common;
using ModShelf.Testing;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the Nexus source has so far, against the recorded API.
/// </summary>
public sealed class NexusSourceTests : SourceContractTests<NexusSource>, IDisposable
{
    private const string TrackedPath = "/v1/user/tracked_mods.json";

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceTests()
    {
        _api.Serve(RecordedNexusApi.Premium, "file");
        _api.Serve(RecordedNexusApi.Premium, "files");
        _api.Serve(RecordedNexusApi.Premium, "download-link");
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.Serve(RecordedNexusApi.Premium, "changelogs");
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");
        ServeGames();

        // When the updates were recorded, so the last day's are recent.
        _api.Time.SetUtcNow(new DateTimeOffset(2026, 10, 23, 22, 59, 0, TimeSpan.Zero));
        KeepTrackedMods();
        _services = _api.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("nexus");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"),
        new("https://www.nexusmods.com/stardewvalley/mods/2400"),
        new("nxm://stardewvalley/mods/2400/files/160380?key=nxm-key&expires=1893456000&user_id=1000001"),
    ];

    protected override TimeProvider Clock => _api.Time;

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override NexusSource CreateSource() => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    /// <summary>
    /// The recorded list of games, four of them, Stardew Valley among them.
    /// </summary>
    private void ServeGames() => _api.Serve(RecordedNexusApi.Premium, "games");

    /// <summary>
    /// Tracked mods as the site keeps them: SMAPI, once tracked, until untracked. Tracking it again, or untracking it
    /// when it isn't tracked, gets a made-up refusal, since how Nexus Mods answers isn't recorded yet.
    /// </summary>
    private void KeepTrackedMods()
    {
        const string Scenario = "tracked-mods";
        const string Tracked = "tracked";
        const string Untracked = "untracked";
        IRequestBuilder list = Request.Create().UsingGet().WithPath(TrackedPath);
        IRequestBuilder track = Request.Create().UsingPost().WithPath(TrackedPath).WithParam("domain_name", "stardewvalley");
        IRequestBuilder untrack = Request.Create().UsingDelete().WithPath(TrackedPath).WithParam("domain_name", "stardewvalley");
        IResponseBuilder none = Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("[]");
        IResponseBuilder smapi = Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("""[{"mod_id": 2400, "domain_name": "stardewvalley"}]""");
        IResponseBuilder refused = Response.Create().WithStatusCode(HttpStatusCode.UnprocessableEntity).WithBody("""{"message": "Not changed"}""");
        IResponseBuilder done = Response.Create().WithStatusCode(HttpStatusCode.OK).WithBody("""{"message": "Done"}""");

        // Nothing is tracked at first. Every answer names the state after it, since WireMock forgets the state otherwise.
        _api.Given(list).InScenario(Scenario).WillSetStateTo(Untracked).RespondWith(none);
        _api.Given(track).InScenario(Scenario).WillSetStateTo(Tracked).RespondWith(done);
        _api.Given(untrack).InScenario(Scenario).WillSetStateTo(Untracked).RespondWith(refused);

        foreach (string state in new[] { Tracked, Untracked })
        {
            bool isTracked = state == Tracked;
            _api.Given(list).InScenario(Scenario).WhenStateIs(state).WillSetStateTo(state).RespondWith(isTracked ? smapi : none);
            _api.Given(track).InScenario(Scenario).WhenStateIs(state).WillSetStateTo(Tracked).RespondWith(isTracked ? refused : done);
            _api.Given(untrack).InScenario(Scenario).WhenStateIs(state).WillSetStateTo(Untracked).RespondWith(isTracked ? done : refused);
        }
    }
}
