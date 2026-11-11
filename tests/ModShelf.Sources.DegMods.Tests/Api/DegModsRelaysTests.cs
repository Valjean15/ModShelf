using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Api;

public sealed class DegModsRelaysTests : IDisposable
{
    private readonly RecordedDegMods _degMods = new();

    [Fact]
    public async Task PutsWhatEachRelayHoldsTogetherEachEventOnce()
    {
        using ServiceProvider services = _degMods.BuildServices();
        NostrFilter nipton = new()
        {
            Authors = [RecordedDegMods.FalloutAuthor],
            Tags = new Dictionary<char, IReadOnlyList<string>> { ['d'] = [RecordedDegMods.Nipton] },
        };

        IReadOnlyList<NostrEvent> events = await services.GetRequiredService<DegModsRelays>().ReadAsync([nipton], TestContext.Current.CancellationToken);

        // The new event on one relay, the older listing on both.
        Assert.Equal([DegModsLink.OldModKind, DegModsLink.ModKind], events.Select(nostrEvent => nostrEvent.Kind).Order());
        Assert.Single(_degMods.Brs.Filters);
        Assert.Single(_degMods.Main.Filters);
    }

    [Fact]
    public async Task UsesTheOthersWhenARelayCantBeReadAndSaysSo()
    {
        _degMods.Brs.Instead = _ => ["not Nostr"];
        using ServiceProvider services = _degMods.BuildServices();

        IReadOnlyList<NostrEvent> events = await services.GetRequiredService<DegModsRelays>().ReadAsync(
            [new NostrFilter { Authors = [RecordedDegMods.VgbAuthor] }], TestContext.Current.CancellationToken);

        Assert.Equal(2, events.Count);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("brs.degmods.com couldn't be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsWhenNoRelayCanBeRead()
    {
        _degMods.Brs.Instead = _ => ["not Nostr"];
        _degMods.Main.Instead = _ => ["not Nostr either"];
        using ServiceProvider services = _degMods.BuildServices();

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => services.GetRequiredService<DegModsRelays>().ReadAsync(
            [new NostrFilter()], TestContext.Current.CancellationToken));

        Assert.Contains("brs.degmods.com", failed.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _degMods.Dispose();
}
