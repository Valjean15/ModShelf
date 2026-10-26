using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// Stardew Valley's updated mods, against the recorded list of the last day and answers written for the other periods.
/// </summary>
public sealed class NexusSourceUpdateTests : IDisposable
{
    private const string UpdatedPath = "/v1/games/stardewvalley/mods/updated.json";

    /// <summary>
    /// About when <c>updated-1d</c> was recorded: its oldest activity is at 22:58 the day before.
    /// </summary>
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 23, 22, 59, 0, TimeSpan.Zero);

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceUpdateTests()
    {
        _api.Time.SetUtcNow(RecordedAt);
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
    public void ReachesBack28Days()
    {
        Assert.Equal(TimeSpan.FromDays(28), Source.Reach);
    }

    [Fact]
    public async Task ListsTheModsActiveInTheLastDay()
    {
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        IReadOnlyList<ModUpdate> updates = await Source.GetUpdatesAsync(StardewValley, RecordedAt.AddDays(-1), Cancellation);

        // Of the 25 recorded, mod 30023 was last active at 22:58:02, a minute before the day began.
        Assert.Equal(24, updates.Count);
        Assert.DoesNotContain(updates, update => update.Mod.Id == "30023");
        Assert.Equal("1d", Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Query!["period"]));
    }

    [Fact]
    public async Task AnUpdateHasTheModsTimesAndPage()
    {
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        IReadOnlyList<ModUpdate> updates = await Source.GetUpdatesAsync(StardewValley, RecordedAt.AddDays(-1), Cancellation);

        // Its page changed that day, but its files more than a year before.
        ModUpdate update = Assert.Single(updates, update => update.Mod.Id == "4856");
        Assert.Equal(new ModRef(StardewValley, "4856"), update.Mod);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 16, 28, 23, TimeSpan.Zero), update.LastActivityAt);
        Assert.Equal(new DateTimeOffset(2025, 4, 16, 2, 45, 43, TimeSpan.Zero), update.FilesUpdatedAt);
        Assert.Equal(new Uri("https://www.nexusmods.com/stardewvalley/mods/4856"), update.PageUrl);
    }

    [Fact]
    public async Task KeepsOnlyTheModsActiveSinceTheTimeAsked()
    {
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        IReadOnlyList<ModUpdate> updates = await Source.GetUpdatesAsync(
            StardewValley, new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.Zero), Cancellation);

        Assert.Equal(["16877", "35013", "35432", "36672", "8785"], updates.Select(update => update.Mod.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AModActiveAtTheVeryTimeAskedIsKept()
    {
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        IReadOnlyList<ModUpdate> updates = await Source.GetUpdatesAsync(
            StardewValley, new DateTimeOffset(2026, 10, 23, 20, 34, 49, TimeSpan.Zero), Cancellation);

        Assert.Equal("16877", Assert.Single(updates).Mod.Id);
    }

    [Theory]
    [InlineData(0, "1d")]
    [InlineData(60 * 60, "1d")]
    [InlineData(24 * 60 * 60, "1d")]
    [InlineData((24 * 60 * 60) + 1, "1w")]
    [InlineData(7 * 24 * 60 * 60, "1w")]
    [InlineData((7 * 24 * 60 * 60) + 1, "1m")]
    [InlineData(28 * 24 * 60 * 60, "1m")]
    public async Task AsksForTheShortestPeriodThatReachesTheTimeAsked(int secondsBack, string period)
    {
        _api.Answer(UpdatedPath, response => response.WithStatusCode(200).WithBody("[]"));

        await Source.GetUpdatesAsync(StardewValley, RecordedAt.AddSeconds(-secondsBack), Cancellation);

        Assert.Equal(period, Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Query!["period"]));
    }

    [Fact]
    public async Task TakesATimeAMomentBeyondItsReach()
    {
        _api.Answer(UpdatedPath, response => response.WithStatusCode(200).WithBody("[]"));

        await Source.GetUpdatesAsync(StardewValley, RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromSeconds(59), Cancellation);

        Assert.Equal("1m", Assert.Single(Assert.Single(_api.Requests).RequestMessage!.Query!["period"]));
    }

    [Theory]
    [InlineData(61)]
    [InlineData(60 * 60 * 24 * 30)]
    public async Task RefusesToLookFurtherBackThan28DaysWithoutAsking(int secondsBeyond)
    {
        ArgumentOutOfRangeException refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Source.GetUpdatesAsync(
            StardewValley, RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromSeconds(secondsBeyond), Cancellation));

        Assert.Equal("since", refused.ParamName);
        Assert.Contains("last 28 days", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task ATimeStillToComeFindsNothing()
    {
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        Assert.Empty(await Source.GetUpdatesAsync(StardewValley, RecordedAt.AddHours(1), Cancellation));
    }

    [Theory]
    [InlineData("someone-else", "stardewvalley")]
    [InlineData("nexus", "Stardew Valley")]
    [InlineData("nexus", "../users")]
    public async Task RefusesAGameNexusModsDoesntNameWithoutAsking(string source, string game)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.GetUpdatesAsync(new GameRef(new SourceId(source), game), RecordedAt.AddHours(-1), Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task SkipsEntriesItCantPlace()
    {
        _api.Answer(UpdatedPath, response => response.WithStatusCode(200).WithBody(
            """
            [
              {"mod_id": 1, "latest_file_update": 1792750000, "latest_mod_activity": 1792750000},
              {"mod_id": 2, "latest_file_update": 0, "latest_mod_activity": 1792750000},
              {"mod_id": 3, "latest_mod_activity": 1792750000},
              {"mod_id": 0, "latest_file_update": 1792750000, "latest_mod_activity": 1792750000},
              {"mod_id": -4, "latest_file_update": 1792750000, "latest_mod_activity": 1792750000},
              {"mod_id": 5, "latest_file_update": 1792750000},
              null
            ]
            """));

        IReadOnlyList<ModUpdate> updates = await Source.GetUpdatesAsync(StardewValley, RecordedAt.AddDays(-1), Cancellation);

        Assert.Equal(["1", "2", "3"], updates.Select(update => update.Mod.Id));
        Assert.Equal([true, false, false], updates.Select(update => update.FilesUpdatedAt.HasValue));
    }

    [Fact]
    public async Task ARefusedAccountIsReported()
    {
        _api.Answer(UpdatedPath, response => response.WithStatusCode(HttpStatusCode.Unauthorized).WithBody("""{"message": "Please provide a valid API Key"}"""));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Source.GetUpdatesAsync(StardewValley, RecordedAt.AddHours(-1), Cancellation));
    }

    [Fact]
    public async Task AGameNexusModsDoesntHaveIsNotFound()
    {
        _api.Answer("/v1/games/notagame/mods/updated.json", response => response.WithStatusCode(HttpStatusCode.NotFound).WithBody("""{"code": 404, "message": "Game not found"}"""));

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.GetUpdatesAsync(new GameRef(new SourceId("nexus"), "notagame"), RecordedAt.AddHours(-1), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task AnAnswerItCantReadIsAFailureToReport()
    {
        _api.Answer(UpdatedPath, response => response.WithStatusCode(200).WithBody("""{"mods": []}"""));

        await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetUpdatesAsync(StardewValley, RecordedAt.AddHours(-1), Cancellation));
    }
}
