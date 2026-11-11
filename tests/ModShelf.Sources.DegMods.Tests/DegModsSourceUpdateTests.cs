using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

public sealed class DegModsSourceUpdateTests : IDisposable
{
    private static readonly GameRef TestGame = new(new SourceId("degmods"), RecordedDegMods.TestGame.ToLowerInvariant());

    private readonly RecordedDegMods _degMods = new();
    private readonly FakeTimeProvider _clock = new(RecordedDegMods.RecordedAt);
    private readonly ServiceProvider _services;

    public DegModsSourceUpdateTests()
    {
        _degMods.AddTestGame();
        _services = _degMods.BuildServices(clock: _clock);
    }

    [Fact]
    public void ReachesBack28Days()
    {
        Assert.Equal(TimeSpan.FromDays(28), Source().Reach);
    }

    [Fact]
    public async Task ReportsTheGamesModsChangedSinceEachWhenItLastDid()
    {
        DateTimeOffset since = RecordedDegMods.RecordedAt.AddDays(-6);

        IReadOnlyList<ModUpdate> updates = await Source().GetUpdatesAsync(TestGame, since, TestContext.Current.CancellationToken);

        // Test mods 55 to 59, the adult one among them: a check needs every watched mod.
        Assert.Equal(5, updates.Count);
        Assert.All(updates, update =>
        {
            Assert.Equal(TestGame, update.Mod.Game);
            Assert.True(update.LastActivityAt >= since);
            Assert.Null(update.FilesUpdatedAt);
            Assert.Equal(DegModsLink.ModPage(DegModsOptions.DefaultSiteAddress, DegModsLink.ModKind, _degMods.Maker.PublicKey, update.Mod.Id.Split(':')[1]), update.PageUrl);
        });
        Assert.Contains(_degMods.Brs.Filters, filter => filter["since"]?.GetValue<long>() == since.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task LeavesOutOtherGamesModsAndThoseItsModerationHides()
    {
        _degMods.Brs.Add(_degMods.Maker.Mod("hidden", "Hidden", RecordedDegMods.TestGame, RecordedDegMods.RecordedAt.AddDays(-1).ToUnixTimeSeconds(), [], ["t", "gore"]));
        _degMods.Brs.Add(_degMods.Maker.Mod("elsewhere", "Elsewhere", "Another game", RecordedDegMods.RecordedAt.AddDays(-1).ToUnixTimeSeconds(), []));
        _degMods.Brs.Add(_degMods.Maker.Mod("shown", "Shown", RecordedDegMods.TestGame, RecordedDegMods.RecordedAt.AddDays(-1).ToUnixTimeSeconds(), []));

        IReadOnlyList<ModUpdate> updates = await Source().GetUpdatesAsync(TestGame, RecordedDegMods.RecordedAt.AddDays(-2), TestContext.Current.CancellationToken);

        // Shown, and test mod 59 of the day before.
        Assert.Equal([$"{_degMods.Maker.PublicKey}:shown", $"{_degMods.Maker.PublicKey}:test-mod-59"], updates.Select(update => update.Mod.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LeavesOutAModChangedInTheSecondAskedButBeforeTheMomentAsked()
    {
        // Test mod 59, the test game's last, was changed two days before the recording, at the start of a second.
        DateTimeOffset last = RecordedDegMods.RecordedAt.AddDays(-2);

        IReadOnlyList<ModUpdate> updates = await Source().GetUpdatesAsync(TestGame, last.AddMilliseconds(500), TestContext.Current.CancellationToken);

        // The relays send it, since they count whole seconds; it was changed before the moment asked, though.
        Assert.Contains(_degMods.Brs.Events, nostrEvent => nostrEvent.D == "test-mod-59" && nostrEvent.CreatedAt == last.ToUnixTimeSeconds());
        Assert.Empty(updates);
        Assert.Contains(_degMods.Brs.Filters, filter => filter["since"]?.GetValue<long>() == last.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task TakesASinceReadAMomentBeforeButNothingFurtherBack()
    {
        DegModsSource source = Source();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await source.GetUpdatesAsync(TestGame, RecordedDegMods.RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromSeconds(30), cancellationToken);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => source.GetUpdatesAsync(TestGame, RecordedDegMods.RecordedAt - TimeSpan.FromDays(28) - TimeSpan.FromMinutes(2), cancellationToken));
    }

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    private DegModsSource Source() => _services.GetServices<IModSource>().OfType<DegModsSource>().Single();
}
