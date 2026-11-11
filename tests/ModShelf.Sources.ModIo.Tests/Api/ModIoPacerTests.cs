using Microsoft.Extensions.Time.Testing;

using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo.Tests.Api;

/// <summary>
/// At most so many requests in any minute, each waiting its turn, and none while held.
/// </summary>
public sealed class ModIoPacerTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly ModIoPacer _pacer;

    public ModIoPacerTests() => _pacer = new ModIoPacer(_clock, perMinute: 2);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _pacer.Dispose();

    [Fact]
    public async Task LetsAsManyGoAsAMinuteAllowsThenWaitsForTheFirstToBeAMinuteOld()
    {
        await _pacer.WaitAsync(Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _pacer.WaitAsync(Cancellation);

        Task third = _pacer.WaitAsync(Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(49));
        await Task.Delay(50, Cancellation);
        bool earlier = third.IsCompleted;
        _clock.Advance(TimeSpan.FromSeconds(1));
        await third;

        Assert.False(earlier);
    }

    [Fact]
    public async Task OneWaitingKeepsTheOthersWaitingBehindIt()
    {
        await _pacer.WaitAsync(Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await _pacer.WaitAsync(Cancellation);

        Task third = _pacer.WaitAsync(Cancellation);
        Task fourth = _pacer.WaitAsync(Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await third;
        await Task.Delay(50, Cancellation);
        bool earlier = fourth.IsCompleted;
        _clock.Advance(TimeSpan.FromSeconds(30));
        await fourth;

        Assert.False(earlier);
    }

    [Fact]
    public async Task AWaitCanBeCancelled()
    {
        await _pacer.WaitAsync(Cancellation);
        await _pacer.WaitAsync(Cancellation);
        using CancellationTokenSource cancel = new();

        Task waiting = _pacer.WaitAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void HoldsAsLongAsAskedOrAMinute()
    {
        DateTimeOffset now = _clock.GetUtcNow();

        Assert.Null(_pacer.HeldUntil);
        Assert.Equal(now.AddSeconds(5), _pacer.Hold(TimeSpan.FromSeconds(5)));
        Assert.Equal(now.AddMinutes(1), _pacer.Hold(TimeSpan.Zero));
        Assert.Equal(now.AddMinutes(1), _pacer.Hold(TimeSpan.FromSeconds(-5)));

        // A shorter hold keeps the longer one.
        Assert.Equal(now.AddMinutes(1), _pacer.Hold(TimeSpan.FromSeconds(30)));
        Assert.Equal(now.AddSeconds(90), _pacer.Hold(now.AddSeconds(90)));
        Assert.Equal(now.AddSeconds(90), _pacer.Hold(now.AddSeconds(-1)));
        Assert.Equal(now.AddSeconds(90), _pacer.HeldUntil);
        _clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Null(_pacer.HeldUntil);
    }
}
