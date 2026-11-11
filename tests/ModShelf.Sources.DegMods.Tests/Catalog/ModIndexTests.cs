using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Catalog;

public sealed class ModIndexTests : IDisposable
{
    private readonly RecordedDegMods _degMods = new();
    private readonly FakeTimeProvider _clock = new(RecordedDegMods.RecordedAt);

    [Fact]
    public async Task ReadsEveryModOnceAndKeepsThemAFewMinutes()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);
        ModIndex index = services.GetRequiredService<ModIndex>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyList<DegMod> first = await index.AllAsync(cancellationToken);
        int asked = _degMods.Brs.Filters.Count;
        _clock.Advance(TimeSpan.FromMinutes(4));
        IReadOnlyList<DegMod> kept = await index.AllAsync(cancellationToken);
        Assert.Equal(asked, _degMods.Brs.Filters.Count);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await index.AllAsync(cancellationToken);

        Assert.Same(first, kept);
        Assert.True(_degMods.Brs.Filters.Count > asked);
    }

    [Fact]
    public async Task ReadsEveryModsNewEventsAndOlderListingsBeforeAugust()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);

        await services.GetRequiredService<ModIndex>().AllAsync(TestContext.Current.CancellationToken);

        Assert.Contains(_degMods.Brs.Filters, filter => filter.ToJsonString() == """{"kinds":[31142],"limit":500}""");
        Assert.Contains(_degMods.Brs.Filters, filter => filter.ToJsonString() == $$"""{"kinds":[30402],"#t":["GameMod"],"until":{{ModEvents.OldModsEnd - 1}},"limit":500}""");
    }

    [Fact]
    public async Task LeavesOutWhatTheAdminBlocksAndKeepsTheRest()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);

        IReadOnlyList<DegMod> mods = await services.GetRequiredService<ModIndex>().AllAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(mods, mod => mod.D == RecordedDegMods.BlockedPost);
        Assert.Contains(mods, mod => mod.D == RecordedDegMods.TaggedPost);
        Assert.Contains(mods, mod => mod.D == RecordedDegMods.Newspapers);
        Assert.Equal(mods.Count, mods.Select(mod => mod.Key).Distinct().Count());
    }

    [Fact]
    public async Task FindsAModAfreshAndSaysWhetherItsShown()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);
        ModIndex index = services.GetRequiredService<ModIndex>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        (ModStatus shown, DegMod? mod) = await index.FindAsync(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, cancellationToken);
        (ModStatus hidden, DegMod? none) = await index.FindAsync(RecordedDegMods.TestPostAuthor, RecordedDegMods.BlockedPost, cancellationToken);
        (ModStatus missing, _) = await index.FindAsync(RecordedDegMods.FalloutAuthor, "no-such-mod", cancellationToken);

        Assert.Equal((ModStatus.Shown, RecordedDegMods.Newspapers), (shown, mod?.D));
        Assert.Equal((ModStatus.Hidden, (DegMod?)null), (hidden, none));
        Assert.Equal(ModStatus.Missing, missing);
    }

    [Fact]
    public async Task FindsTheAuthorsModThoughAnotherHasOneWithTheSameDTag()
    {
        _degMods.Brs.Add(new TestAuthor(4).Mod(RecordedDegMods.Newspapers, "Someone else's", "Another game", 1_800_000_000, []));

        // A relay sends it though the filter names the author: ModShelf doesn't count on a relay to filter.
        _degMods.Brs.IgnoresAuthors = true;
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);

        (ModStatus status, DegMod? mod) = await services.GetRequiredService<ModIndex>().FindAsync(
            RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, TestContext.Current.CancellationToken);

        Assert.Equal((ModStatus.Shown, RecordedDegMods.FalloutAuthor), (status, mod?.Author));
    }

    [Fact]
    public async Task ReadsTheModerationWithAModUnlessItWasReadAFewMinutesAgo()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);
        ModIndex index = services.GetRequiredService<ModIndex>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await index.FindAsync(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, cancellationToken);
        int first = _degMods.Main.Filters.Count;
        await index.FindAsync(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, cancellationToken);
        int second = _degMods.Main.Filters.Count - first;
        _clock.Advance(TimeSpan.FromMinutes(6));
        await index.FindAsync(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, cancellationToken);
        int third = _degMods.Main.Filters.Count - first - second;

        Assert.True(first > 1, $"Asked {first} filters.");
        Assert.Equal(1, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public async Task ReadsWhatChangedSinceATimeWithoutOlderListingsWhenItsSinceAugust()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);
        ModIndex index = services.GetRequiredService<ModIndex>();
        DateTimeOffset since = DateTimeOffset.FromUnixTimeSeconds(1786000000);

        IReadOnlyList<DegMod> changed = await index.ChangedSinceAsync(since, TestContext.Current.CancellationToken);

        Assert.NotEmpty(changed);
        Assert.All(changed, mod => Assert.True(mod.UpdatedAt >= since));
        Assert.Contains(_degMods.Brs.Filters, filter => filter.ToJsonString() == """{"kinds":[31142],"since":1786000000,"limit":500}""");
        Assert.DoesNotContain(_degMods.Brs.Filters, filter => filter["kinds"]![0]!.GetValue<int>() == 30402);
    }

    [Fact]
    public async Task ReadsOlderListingsTooWhenWhatChangedReachesBeforeAugust()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: _clock);
        DateTimeOffset since = DateTimeOffset.FromUnixTimeSeconds(ModEvents.OldModsEnd - 10);

        await services.GetRequiredService<ModIndex>().ChangedSinceAsync(since, TestContext.Current.CancellationToken);

        Assert.Contains(_degMods.Brs.Filters, filter => filter.ToJsonString() == $$"""{"kinds":[30402],"#t":["GameMod"],"since":{{ModEvents.OldModsEnd - 10}},"until":{{ModEvents.OldModsEnd - 1}},"limit":500}""");
    }

    public void Dispose() => _degMods.Dispose();
}
