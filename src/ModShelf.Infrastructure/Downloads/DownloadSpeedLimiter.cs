using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Keeps ModShelf's downloads together under the speed limit the settings set (ADR 0105): a token bucket that every
/// download draws from as it reads, refilled at the limit, holding a quarter of a second's worth. A download that reads
/// more than the bucket holds waits until the bytes fit, so several at once share the limit between them. The limit is
/// read again every few seconds, so a change made anywhere, such as from the command line, counts while downloads run.
/// </summary>
internal sealed partial class DownloadSpeedLimiter
{
    /// <summary>
    /// How long a limit read is used before it's read again.
    /// </summary>
    internal static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(2);

    // The least a read takes under a limit, so a low limit doesn't make reads of a few bytes.
    private const int SmallestRead = 4096;

    private readonly Func<CancellationToken, Task<long?>> _readLimit;
    private readonly TimeProvider _time;
    private readonly ILogger<DownloadSpeedLimiter> _logger;
    private readonly Lock _gate = new();

    private bool _limitRead;
    private long? _limit;
    private long _limitReadAt;

    // Bytes that can be taken at once; below 0, what's been taken ahead of the limit.
    private double _tokens;
    private long _filledAt;

    /// <param name="readLimit">The limit in force, in bytes a second; null for none.</param>
    public DownloadSpeedLimiter(Func<CancellationToken, Task<long?>> readLimit, TimeProvider time, ILogger<DownloadSpeedLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(readLimit);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _readLimit = readLimit;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// A limiter that never limits, for a host without settings.
    /// </summary>
    public static DownloadSpeedLimiter None(TimeProvider time)
        => new(_ => Task.FromResult<long?>(null), time, NullLogger<DownloadSpeedLimiter>.Instance);

    /// <summary>
    /// How many bytes to read at once, up to <paramref name="buffer"/>: under a limit, about a quarter of a second's worth,
    /// so progress stays smooth and a change of the limit counts soon.
    /// </summary>
    public async ValueTask<int> ReadSizeAsync(int buffer, CancellationToken cancellationToken)
    {
        long? limit = await LimitAsync(cancellationToken).ConfigureAwait(false);
        return limit is { } bytesPerSecond ? (int)Math.Clamp(bytesPerSecond / 4, Math.Min(SmallestRead, buffer), buffer) : buffer;
    }

    /// <summary>
    /// Takes <paramref name="bytes"/> just read from the bucket, waiting until they fit under the limit; at once when
    /// there's none.
    /// </summary>
    public async ValueTask TakeAsync(int bytes, CancellationToken cancellationToken)
    {
        if (await LimitAsync(cancellationToken).ConfigureAwait(false) is not { } limit)
        {
            return;
        }

        TimeSpan wait;
        lock (_gate)
        {
            long now = _time.GetTimestamp();
            double burst = limit / 4.0;
            _tokens = Math.Min(burst, _tokens + (_time.GetElapsedTime(_filledAt, now).TotalSeconds * limit));
            _filledAt = now;
            _tokens -= bytes;
            wait = _tokens >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-_tokens / limit);
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The limit in force: as read within <see cref="ReadEvery"/>, or read now. A limit that can't be read, such as from
    /// settings being edited by hand, leaves the one before.
    /// </summary>
    private async ValueTask<long?> LimitAsync(CancellationToken cancellationToken)
    {
        long now = _time.GetTimestamp();
        lock (_gate)
        {
            if (_limitRead && _time.GetElapsedTime(_limitReadAt, now) < ReadEvery)
            {
                return _limit;
            }

            // Others meanwhile use the limit read before, rather than all reading it.
            _limitReadAt = now;
        }

        long? limit;
        try
        {
            limit = await _readLimit(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LogLimitUnread(_logger, exception);
            lock (_gate)
            {
                return _limit;
            }
        }

        lock (_gate)
        {
            bool first = !_limitRead;
            long? before = _limit;
            _limitRead = true;
            _limit = limit;
            if (limit != before)
            {
                // A limit set or changed starts full, as one that's on after a pause does.
                _tokens = (limit ?? 0) / 4.0;
                _filledAt = now;
                if (limit is { } bytesPerSecond)
                {
                    LogLimited(_logger, bytesPerSecond);
                }
                else if (!first)
                {
                    LogUnlimited(_logger);
                }
            }

            return limit;
        }
    }
}
