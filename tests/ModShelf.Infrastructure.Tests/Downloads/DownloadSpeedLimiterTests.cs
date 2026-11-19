using Microsoft.Extensions.Time.Testing;

using ModShelf.Infrastructure.Downloads;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Downloads;

/// <summary>
/// The speed limit downloads share, at 100 KB a second unless a test says otherwise, on a clock the test moves: the
/// bucket holds a quarter of a second's worth, 25 KB.
/// </summary>
public sealed class DownloadSpeedLimiterTests
{
    private const int Limit = 100 * 1024;
    private const int Quarter = Limit / 4;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<DownloadSpeedLimiter> _logger = new();
    private long? _limit = Limit;
    private Exception? _unreadable;
    private int _reads;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutALimitNothingWaitsAndReadsTakeTheWholeBuffer()
    {
        _limit = null;
        DownloadSpeedLimiter limiter = Limiter();

        Task taking = limiter.TakeAsync(10 * 1024 * 1024, Cancellation).AsTask();

        Assert.True(taking.IsCompletedSuccessfully);
        Assert.Equal(81920, await limiter.ReadSizeAsync(81920, Cancellation));
        Assert.DoesNotContain(_logger.Entries, entry => entry.StartsWith("Information: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADownloadWaitsUntilWhatItReadFitsUnderTheLimit()
    {
        DownloadSpeedLimiter limiter = Limiter();

        // A quarter of a second's worth goes at once; the rest of 80 KB takes 0.55 seconds at 100 KB a second.
        Task taking = limiter.TakeAsync(80 * 1024, Cancellation).AsTask();
        Assert.False(taking.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(549));
        Assert.False(taking.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await taking;

        Assert.Equal(Quarter, await limiter.ReadSizeAsync(81920, Cancellation));
        Assert.Equal(["Information: Downloads are limited to 102400 bytes a second together"], _logger.Entries.Select(entry => entry.TrimEnd()));
    }

    [Fact]
    public async Task DownloadsAtOnceShareTheLimit()
    {
        DownloadSpeedLimiter limiter = Limiter();

        Task[] taking = [.. Enumerable.Range(0, 3).Select(_ => limiter.TakeAsync(Quarter, Cancellation).AsTask())];

        // 75 KB in half a second, with the quarter the bucket held: 100 KB a second together.
        Assert.Equal([true, false, false], taking.Select(task => task.IsCompleted));
        _time.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal([true, true, false], taking.Select(task => task.IsCompleted));
        _time.Advance(TimeSpan.FromMilliseconds(250));
        await Task.WhenAll(taking);
    }

    [Fact]
    public async Task OverTimeADownloadTakesNoMoreThanTheLimit()
    {
        DownloadSpeedLimiter limiter = Limiter();
        DateTimeOffset start = _time.GetUtcNow();
        long taken = 0;

        // As a download reads: what the limit lets it read at once, then waits for it.
        while (taken < 1024 * 1024)
        {
            int read = await limiter.ReadSizeAsync(81920, Cancellation);
            Task taking = limiter.TakeAsync(read, Cancellation).AsTask();
            while (!taking.IsCompleted)
            {
                _time.Advance(TimeSpan.FromMilliseconds(10));
            }

            await taking;
            taken += read;
        }

        // A megabyte at 100 KB a second, but for the quarter the bucket held at the start.
        TimeSpan took = _time.GetUtcNow() - start;
        Assert.InRange(took.TotalSeconds, 9.7, 10.0);
    }

    [Fact]
    public async Task AChangedLimitCountsOnceItsReadAgainAndALimitTurnedOffStopsWaiting()
    {
        DownloadSpeedLimiter limiter = Limiter();
        await limiter.TakeAsync(Quarter, Cancellation);

        _limit = Limit / 2;
        _time.Advance(DownloadSpeedLimiter.ReadEvery - TimeSpan.FromMilliseconds(1));
        Assert.Equal(Quarter, await limiter.ReadSizeAsync(81920, Cancellation));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Quarter / 2, await limiter.ReadSizeAsync(81920, Cancellation));

        // A changed limit starts with its own quarter of a second: 12.5 KB, then 37.5 KB more at 50 KB a second.
        Task halved = limiter.TakeAsync(50 * 1024, Cancellation).AsTask();
        _time.Advance(TimeSpan.FromMilliseconds(749));
        Assert.False(halved.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await halved;

        _limit = null;
        _time.Advance(DownloadSpeedLimiter.ReadEvery);
        Assert.True(limiter.TakeAsync(10 * 1024 * 1024, Cancellation).AsTask().IsCompletedSuccessfully);

        Assert.Equal(
            [
                "Information: Downloads are limited to 102400 bytes a second together",
                "Information: Downloads are limited to 51200 bytes a second together",
                "Information: Downloads are no longer limited",
            ],
            _logger.Entries.Select(entry => entry.TrimEnd()));
        Assert.Equal(3, _reads);
    }

    [Fact]
    public async Task ALimitThatCantBeReadLeavesTheOneBefore()
    {
        DownloadSpeedLimiter limiter = Limiter();
        await limiter.ReadSizeAsync(81920, Cancellation);
        _unreadable = new InvalidDataException("The settings can't be read.");

        _time.Advance(DownloadSpeedLimiter.ReadEvery);
        int read = await limiter.ReadSizeAsync(81920, Cancellation);

        Assert.Equal(Quarter, read);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith(
            "Warning: Couldn't read the download speed limit, so the one before stays System.IO.InvalidDataException: The settings can't be read.",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALowLimitStillReadsAFewKilobytesAtOnce()
    {
        _limit = 1024;

        Assert.Equal(4096, await Limiter().ReadSizeAsync(81920, Cancellation));
    }

    private DownloadSpeedLimiter Limiter() => new(
        _ =>
        {
            _reads++;
            return _unreadable is { } failure ? Task.FromException<long?>(failure) : Task.FromResult(_limit);
        },
        _time,
        _logger);
}
