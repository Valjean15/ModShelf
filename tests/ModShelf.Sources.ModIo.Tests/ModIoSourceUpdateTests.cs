using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// A game's mods whose live file changed since a time, from its events, and what a mod depends on, as recorded.
/// </summary>
public sealed class ModIoSourceUpdateTests : IDisposable
{
    private const string Events = "/v1/games/6715/mods/events?event_type=MODFILE_CHANGED&date_added-min=";

    private readonly ModIoHarness _harness = new(RecordedModIoApi.AccessToken);

    public ModIoSourceUpdateTests() => _harness.Api.ServeAll();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ReachesBack28Days()
    {
        Assert.Equal(TimeSpan.FromDays(28), _harness.Source.Reach);

        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(
            ModIoHarness.Game, RecordedModIoApi.RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromSeconds(59), ModIoHarness.Cancellation);

        Assert.NotEmpty(updates);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _harness.Source.GetUpdatesAsync(
            ModIoHarness.Game, RecordedModIoApi.RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromSeconds(61), ModIoHarness.Cancellation));
    }

    [Fact]
    public async Task ReportsEachModWhoseFileChangedWhenItLastDid()
    {
        DateTimeOffset since = DateTimeOffset.FromUnixTimeSeconds(RecordedModIoApi.ChangesSince);

        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, since, ModIoHarness.Cancellation);

        // The recording holds the newest 100 of 552 file changes; the page after it, not recorded, holds none.
        Assert.Equal([$"{Events}{RecordedModIoApi.ChangesSince}&_limit=100", $"{Events}{RecordedModIoApi.ChangesSince}&_limit=100&_offset=100"], _harness.Asked);
        Assert.Equal(100, updates.Count);
        ModUpdate latest = Assert.Single(updates, update => update.Mod.Id == "6250357");
        Assert.Equal((ModIoHarness.Game, DateTimeOffset.FromUnixTimeSeconds(1794396210)), (latest.Mod.Game, latest.LastActivityAt));
        Assert.Equal((latest.LastActivityAt, (Uri?)null), (latest.FilesUpdatedAt, latest.PageUrl));
    }

    [Fact]
    public async Task AsksFromTheSecondTheTimeIsInAndKeepsWhatCameSince()
    {
        DateTimeOffset since = DateTimeOffset.FromUnixTimeSeconds(1794396210).AddMilliseconds(500);

        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, since, ModIoHarness.Cancellation);

        Assert.Equal($"{Events}1794396210&_limit=100", Assert.Single(_harness.Asked));
        Assert.Empty(updates);
    }

    [Fact]
    public async Task AModChangedSeveralTimesIsReportedOnceAtItsLastChange()
    {
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody("""
            {"data": [
              {"id": 3, "mod_id": 1, "date_added": 1794390000, "event_type": "MODFILE_CHANGED"},
              {"id": 2, "mod_id": 2, "date_added": 1794380000, "event_type": "MODFILE_CHANGED"},
              {"id": 1, "mod_id": 1, "date_added": 1794370000, "event_type": "MODFILE_CHANGED"},
              {"id": 0, "mod_id": 0, "date_added": 1794370000, "event_type": "MODFILE_CHANGED"}
            ], "result_total": 4}
            """));

        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-1), ModIoHarness.Cancellation);

        Assert.Equal(
            [("1", 1794390000L), ("2", 1794380000L)],
            updates.Select(update => (update.Mod.Id, update.LastActivityAt.ToUnixTimeSeconds())).Order());
    }

    [Fact]
    public async Task ReadsEveryPageOfChanges()
    {
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "mod_id": {{id}}, "date_added": 1794390000 }"""))}}], "result_total": 150}""";
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody(page));

        await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-1), ModIoHarness.Cancellation);

        Assert.Equal(2, _harness.Asked.Count);
        Assert.EndsWith("&_limit=100&_offset=100", _harness.Asked[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageOfChangesShortOfFullIsTheLast()
    {
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody(
            """{"data": [{"id": 1, "mod_id": 1, "date_added": 1794390000}], "result_total": 3}"""));

        await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-1), ModIoHarness.Cancellation);

        Assert.Single(_harness.Asked);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(5000)]
    public async Task MoreChangesThanItReadsAreBeyondItsReach(int total)
    {
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody($$"""{"data": [], "result_total": {{total}} }"""));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-20), ModIoHarness.Cancellation));

        Assert.Single(_harness.Asked);
        Assert.Contains(_harness.Api.Logs.Entries, entry => entry.Contains($"mod.io lists {total} file changes of game 6715", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangesThatKeepComingPastTenPagesAreBeyondItsReach()
    {
        string events = string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "mod_id": {{id}}, "date_added": 1794390000 }"""));
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody($$"""{"data": [{{events}}], "result_total": 1000}"""));
        _harness.Api.Answer("/v1/games/6715/mods/events", "_offset", answer => answer.WithBody($$"""{"data": [{{events}}], "result_total": 2000}"""));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-1), ModIoHarness.Cancellation));

        Assert.Equal(10, _harness.Asked.Count);
    }

    [Fact]
    public async Task ReadsTenPagesOfChangesAtMost()
    {
        string page = $$"""{"data": [{{string.Join(", ", Enumerable.Range(1, 100).Select(id => $$"""{"id": {{id}}, "mod_id": {{id}}, "date_added": 1794390000 }"""))}}], "result_total": 1000}""";
        _harness.Api.Answer("/v1/games/6715/mods/events", answer => answer.WithBody(page));

        IReadOnlyList<ModUpdate> updates = await _harness.Source.GetUpdatesAsync(ModIoHarness.Game, RecordedModIoApi.RecordedAt.AddDays(-1), ModIoHarness.Cancellation);

        Assert.Equal(10, _harness.Asked.Count);
        Assert.Equal(100, updates.Count);
    }

    [Fact]
    public async Task ListsTheModsAModDependsOn()
    {
        IReadOnlyList<ModRequirement> needs = await _harness.Source.GetRequirementsAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal("/v1/games/6715/mods/4228735/dependencies", Assert.Single(_harness.Asked));
        ModRequirement impui = Assert.Single(needs);
        Assert.Equal(
            ("ImpUI (ImprovedUI)", new ModRef(ModIoHarness.Game, "4138213"), new Uri("https://mod.io/g/baldursgate3/m/impui"), (string?)null),
            (impui.Name, impui.Mod, impui.PageUrl, impui.Notes));
    }

    [Fact]
    public async Task ADependencyWithoutANameOrGameIsNamedByItsIdOfTheModsGame()
    {
        _harness.Api.Answer("/v1/games/6715/mods/4228735/dependencies", answer => answer.WithBody(
            """{"data": [{"id": 7, "name": " "}, {"id": 8, "game_id": 306, "name": "Other", "profile_url": "javascript:alert(1)"}, {"id": 0, "name": "None"}], "result_total": 3}"""));

        IReadOnlyList<ModRequirement> needs = await _harness.Source.GetRequirementsAsync(ModIoHarness.Mod, ModIoHarness.Cancellation);

        Assert.Equal(
            [("Mod 7", new ModRef(ModIoHarness.Game, "7"), (Uri?)null), ("Other", new ModRef(new GameRef(ModIoHarness.ModIo, "306"), "8"), null)],
            needs.Select(need => (need.Name, need.Mod, need.PageUrl)));
    }
}
