using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Tests.Common;

using WireMock.Logging;
using WireMock.ResponseBuilders;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The account's tracked mods, against the recorded list and answers written for tracking and untracking.
/// </summary>
/// <remarks>
/// How Nexus Mods answers tracking a mod already tracked, or untracking one that isn't, isn't documented; the source
/// checks the list whenever it refuses, so these tests answer with made-up statuses.
/// </remarks>
public sealed class NexusSourceWatchlistTests : IDisposable
{
    private const string TrackedPath = "/v1/user/tracked_mods.json";

    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceWatchlistTests()
    {
        _services = _api.BuildServices();
    }

    public static TheoryData<string> Changes { get; } = ["track", "untrack"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public async Task TheRecordedAccountTracksNothing()
    {
        _api.Serve(RecordedNexusApi.Premium, "tracked-mods");

        Assert.Empty(await Source.GetWatchedAsync(Cancellation));
    }

    [Fact]
    public async Task ListsTheTrackedModsOfEveryGame()
    {
        _api.Answer(TrackedPath, Tracking("""{"mod_id": 2400, "domain_name": "stardewvalley"}, {"mod_id": 12604, "domain_name": "SkyrimSpecialEdition"}"""));

        IReadOnlyList<ModRef> tracked = await Source.GetWatchedAsync(Cancellation);

        Assert.Equal([Smapi, new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604")], tracked);
    }

    [Fact]
    public async Task SkipsEntriesItCantPlaceAndListsEachModOnce()
    {
        _api.Answer(TrackedPath, Tracking(
            """
            {"mod_id": 2400, "domain_name": "stardewvalley"},
            {"mod_id": 2400, "domain_name": "StardewValley"},
            {"mod_id": 0, "domain_name": "stardewvalley"},
            {"mod_id": -1, "domain_name": "stardewvalley"},
            {"mod_id": 3, "domain_name": "stardew valley"},
            {"mod_id": 4, "domain_name": "../users"},
            {"mod_id": 5},
            null
            """));

        Assert.Equal([Smapi], await Source.GetWatchedAsync(Cancellation));
    }

    [Fact]
    public async Task TracksAModWithAFormPostThatNamesItsGame()
    {
        _api.AnswerPost(TrackedPath, Says(HttpStatusCode.Created, "User 1000001 is now Tracking Mod: 2400"));

        await Source.WatchAsync(Smapi, Cancellation);

        ILogEntry request = Assert.Single(_api.Requests);
        Assert.Equal("POST", request.RequestMessage!.Method);
        AssertNamesSmapi(request);
    }

    [Fact]
    public async Task UntracksAModWithAFormDeleteThatNamesItsGame()
    {
        _api.AnswerDelete(TrackedPath, Says(HttpStatusCode.OK, "User 1000001 is no longer tracking 2400"));

        await Source.UnwatchAsync(Smapi, Cancellation);

        ILogEntry request = Assert.Single(_api.Requests);
        Assert.Equal("DELETE", request.RequestMessage!.Method);
        AssertNamesSmapi(request);
    }

    [Fact]
    public async Task TracksAModAsNexusModsAnswered()
    {
        _api.Serve(RecordedNexusApi.Premium, "track");

        await Source.WatchAsync(Smapi, Cancellation);

        Assert.Equal("POST", Assert.Single(_api.Requests).RequestMessage!.Method);
    }

    [Fact]
    public async Task TrackingAModAgainIsAnsweredAsTheFirstTime()
    {
        // Nexus Mods answers 201 again, as for a mod not tracked yet.
        _api.Serve(RecordedNexusApi.Premium, "track-tracked");

        await Source.WatchAsync(Smapi, Cancellation);

        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task UntracksAModAsNexusModsAnswered()
    {
        _api.Serve(RecordedNexusApi.Premium, "untrack");

        await Source.UnwatchAsync(Smapi, Cancellation);

        Assert.Equal("DELETE", Assert.Single(_api.Requests).RequestMessage!.Method);
    }

    [Fact]
    public async Task UntrackingAModNotTrackedIsAnsweredNotFoundButIsntAnError()
    {
        _api.Serve(RecordedNexusApi.Premium, "untrack-not-tracked");
        _api.Serve(RecordedNexusApi.Premium, "tracked-mods");

        await Source.UnwatchAsync(Smapi, Cancellation);

        Assert.Equal(["DELETE", "GET"], _api.Requests.Select(request => request.RequestMessage!.Method));
    }

    [Fact]
    public async Task TrackingAModNexusModsDoesntHaveSaysSo()
    {
        _api.Serve(RecordedNexusApi.Premium, "track-unknown-mod");
        _api.Serve(RecordedNexusApi.Premium, "tracked-mods");

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.WatchAsync(new ModRef(Smapi.Game, "999999999"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains("Mod: 999999999 not found", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsAModTrackedAsNexusModsListedIt()
    {
        _api.Serve(RecordedNexusApi.Premium, "tracked-mods-with-mod");

        Assert.Equal([Smapi], await Source.GetWatchedAsync(Cancellation));
    }

    [Fact]
    public async Task TracksAModItsGameNamedInAnyCase()
    {
        _api.AnswerPost(TrackedPath, Says(HttpStatusCode.Created, "Tracking"));

        await Source.WatchAsync(new ModRef(new GameRef(new SourceId("nexus"), "StardewValley"), "2400"), Cancellation);

        AssertNamesSmapi(Assert.Single(_api.Requests));
    }

    [Fact]
    public async Task TrackingAModAlreadyTrackedIsntAnError()
    {
        _api.AnswerPost(TrackedPath, Says(HttpStatusCode.UnprocessableEntity, "User 1000001 is already Tracking Mod: 2400"));
        _api.Answer(TrackedPath, Tracking("""{"mod_id": 2400, "domain_name": "stardewvalley"}"""));

        await Source.WatchAsync(Smapi, Cancellation);

        Assert.Equal(["POST", "GET"], _api.Requests.Select(request => request.RequestMessage!.Method));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task UntrackingAModNotTrackedIsntAnError(HttpStatusCode status)
    {
        _api.AnswerDelete(TrackedPath, Says(status, "User 1000001 is not tracking 2400"));
        _api.Answer(TrackedPath, Tracking("""{"mod_id": 12604, "domain_name": "skyrimspecialedition"}"""));

        await Source.UnwatchAsync(Smapi, Cancellation);

        Assert.Equal(["DELETE", "GET"], _api.Requests.Select(request => request.RequestMessage!.Method));
    }

    [Fact]
    public async Task TrackingAModThatDoesntExistIsNotFound()
    {
        _api.AnswerPost(TrackedPath, Says(HttpStatusCode.NotFound, "Mod not found"));
        _api.Answer(TrackedPath, Tracking(string.Empty));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.WatchAsync(Smapi, Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains("Mod not found", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusalToTrackStandsWhenTheModIsntTracked()
    {
        _api.AnswerPost(TrackedPath, Says(HttpStatusCode.Forbidden, "Not allowed"));
        _api.Answer(TrackedPath, Tracking("""{"mod_id": 12604, "domain_name": "skyrimspecialedition"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.WatchAsync(Smapi, Cancellation));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task ARefusalToUntrackStandsWhenTheModIsStillTracked()
    {
        _api.AnswerDelete(TrackedPath, Says(HttpStatusCode.BadRequest, "Bad request"));
        _api.Answer(TrackedPath, Tracking("""{"mod_id": 2400, "domain_name": "stardewvalley"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Source.UnwatchAsync(Smapi, Cancellation));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task ARefusedAccountIsReportedWithoutAskingAgain(string change)
    {
        AnswerChange(change, Says(HttpStatusCode.Unauthorized, "Please provide a valid API Key"));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => ChangeAsync(change));

        Assert.Single(_api.Requests);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task AUsedUpRateLimitIsReportedWithoutAskingAgain(string change)
    {
        AnswerChange(change, Says(HttpStatusCode.TooManyRequests, "Rate limit exceeded"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => ChangeAsync(change));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Single(_api.Requests);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task AServerErrorIsReportedWithoutCheckingTheList(string change)
    {
        AnswerChange(change, Says(HttpStatusCode.ServiceUnavailable, "Down for maintenance"));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => ChangeAsync(change));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.DoesNotContain(_api.Requests, request => request.RequestMessage!.Method == "GET");
    }

    [Theory]
    [InlineData("someone-else", "stardewvalley", "2400")]
    [InlineData("nexus", "stardew valley", "2400")]
    [InlineData("nexus", "stardewvalley", "SMAPI")]
    [InlineData("nexus", "stardewvalley", "0")]
    public async Task RefusesAModNexusModsDoesntNameWithoutAsking(string source, string game, string mod)
    {
        ModRef refused = new(new GameRef(new SourceId(source), game), mod);

        await Assert.ThrowsAsync<ArgumentException>(() => Source.WatchAsync(refused, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Source.UnwatchAsync(refused, Cancellation));
        Assert.Empty(_api.Requests);
    }

    private static Func<IResponseBuilder, IResponseBuilder> Tracking(string entries)
        => response => response.WithStatusCode(HttpStatusCode.OK).WithBody($"[{entries}]");

    private static Func<IResponseBuilder, IResponseBuilder> Says(HttpStatusCode status, string message)
        => response => response.WithStatusCode(status).WithBody($$"""{"message": "{{message}}"}""");

    private static void AssertNamesSmapi(ILogEntry request)
    {
        Assert.Equal(TrackedPath, request.RequestMessage!.Path);
        Assert.Equal("stardewvalley", Assert.Single(request.RequestMessage.Query!["domain_name"]));
        Assert.Equal("mod_id=2400", request.RequestMessage.Body);
        Assert.StartsWith("application/x-www-form-urlencoded", Assert.Single(request.RequestMessage.Headers!["Content-Type"]), StringComparison.Ordinal);
    }

    private void AnswerChange(string change, Func<IResponseBuilder, IResponseBuilder> response)
    {
        if (change == "track")
        {
            _api.AnswerPost(TrackedPath, response);
        }
        else
        {
            _api.AnswerDelete(TrackedPath, response);
        }

        _api.Answer(TrackedPath, Tracking(string.Empty));
    }

    private Task ChangeAsync(string change)
        => change == "track" ? Source.WatchAsync(Smapi, Cancellation) : Source.UnwatchAsync(Smapi, Cancellation);
}
