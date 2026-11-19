using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public async Task ARelayThatDoesntAnswerInTimeRestsFiveMinutesSaidOnceThenIsAskedAgain()
    {
        int asked = 0;
        TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _degMods.Brs.Instead = _ =>
        {
            Interlocked.Increment(ref asked);
            waiting.TrySetResult();
            return [];
        };
        FakeTimeProvider clock = new(RecordedDegMods.RecordedAt);
        using ServiceProvider services = _degMods.BuildServices(clock: clock);
        DegModsRelays relays = services.GetRequiredService<DegModsRelays>();
        NostrFilter filter = new() { Authors = [RecordedDegMods.VgbAuthor] };

        Task<IReadOnlyList<NostrEvent>> late = relays.ReadAsync([filter], Cancellation);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        await ReadFromAsync("relay.degmods.com", 1);
        await TimeOutAsync(clock, late);
        IReadOnlyList<NostrEvent> first = await late;

        // Resting, it isn't asked, and the read doesn't wait.
        IReadOnlyList<NostrEvent> resting = await relays.ReadAsync([filter], Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        int askedResting = asked;
        clock.Advance(DegModsRelays.RestFor);
        _degMods.Brs.Instead = null;
        IReadOnlyList<NostrEvent> again = await relays.ReadAsync([filter], Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, resting.Count);
        Assert.Equal(1, askedResting);
        Assert.Equal(2, again.Count);
        Assert.Single(_degMods.Brs.Filters);
        Assert.Single(_degMods.Logs.Entries, entry => entry.Contains("brs.degmods.com didn't answer in time, so it isn't asked for 5 minutes", StringComparison.Ordinal));
        Assert.DoesNotContain(_degMods.Logs.Entries, entry => entry.Contains("couldn't be read", StringComparison.Ordinal));
        Assert.Single(_degMods.Logs.Entries, entry => entry.Contains("brs.degmods.com answers again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenEveryRelayRestsTheyreAllAsked()
    {
        int asked = 0;
        Func<string, IReadOnlyList<string>?> silent = _ =>
        {
            Interlocked.Increment(ref asked);
            return [];
        };
        _degMods.Brs.Instead = silent;
        _degMods.Main.Instead = silent;
        FakeTimeProvider clock = new(RecordedDegMods.RecordedAt);
        using ServiceProvider services = _degMods.BuildServices(clock: clock);
        DegModsRelays relays = services.GetRequiredService<DegModsRelays>();
        NostrFilter filter = new() { Authors = [RecordedDegMods.VgbAuthor] };

        Task<IReadOnlyList<NostrEvent>> late = relays.ReadAsync([filter], Cancellation);
        await WaitUntilAsync(() => Volatile.Read(ref asked) == 2);
        await TimeOutAsync(clock, late);
        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => late);
        _degMods.Brs.Instead = null;
        _degMods.Main.Instead = null;

        IReadOnlyList<NostrEvent> events = await relays.ReadAsync([filter], Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        Assert.IsType<TimeoutException>(failed.InnerException);
        Assert.Equal(2, events.Count);
        Assert.Equal(2, _degMods.Logs.Entries.Count(entry => entry.Contains("answers again", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARelayThatCantBeReachedIsAskedEachTime()
    {
        _degMods.Brs.Instead = _ => ["not Nostr"];
        using ServiceProvider services = _degMods.BuildServices();
        DegModsRelays relays = services.GetRequiredService<DegModsRelays>();

        await relays.ReadAsync([new NostrFilter { Authors = [RecordedDegMods.VgbAuthor] }], Cancellation);
        await relays.ReadAsync([new NostrFilter { Authors = [RecordedDegMods.VgbAuthor] }], Cancellation);

        Assert.Equal(2, _degMods.Logs.Entries.Count(entry => entry.Contains("brs.degmods.com couldn't be read", StringComparison.Ordinal)));
        Assert.DoesNotContain(_degMods.Logs.Entries, entry => entry.Contains("isn't asked", StringComparison.Ordinal));
    }

    public void Dispose() => _degMods.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        giveUp.CancelAfter(TimeSpan.FromSeconds(30));
        while (!done())
        {
            await Task.Delay(10, giveUp.Token);
        }
    }

    /// <summary>
    /// Moves the clock past a relay's time to answer until <paramref name="reading"/> ends: the client may not be
    /// waiting for the answer yet when the relay has the request.
    /// </summary>
    private static async Task TimeOutAsync(FakeTimeProvider clock, Task reading)
    {
        using CancellationTokenSource giveUp = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        giveUp.CancelAfter(TimeSpan.FromSeconds(30));
        while (!reading.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            await Task.WhenAny(reading, Task.Delay(50, giveUp.Token));
        }
    }

    /// <summary>
    /// Waits until the relay on <paramref name="host"/> has been read <paramref name="times"/> times, as its client logs.
    /// </summary>
    private Task ReadFromAsync(string host, int times)
        => WaitUntilAsync(() => _degMods.Logs.Entries.Count(entry => entry.Contains("events from DEG Mods' relay", StringComparison.Ordinal)
            && entry.Contains(host, StringComparison.Ordinal)) >= times);
}
