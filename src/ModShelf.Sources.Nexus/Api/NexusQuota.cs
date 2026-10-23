using System.Globalization;
using System.Net.Http.Headers;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// The API's rate limit for the account in use, as its last response reported it.
/// </summary>
/// <remarks>
/// Nexus Mods counts requests per account: a daily allowance, and once that's used up, an hourly one. Every response
/// carries the <c>X-RL-*</c> headers with what's left of each and when it resets. Requests can go out while either
/// allowance has some left.
/// </remarks>
internal sealed class NexusQuota(TimeProvider timeProvider)
{
    private const string HourlyLimitHeader = "X-RL-Hourly-Limit";
    private const string HourlyRemainingHeader = "X-RL-Hourly-Remaining";
    private const string HourlyResetHeader = "X-RL-Hourly-Reset";
    private const string DailyLimitHeader = "X-RL-Daily-Limit";
    private const string DailyRemainingHeader = "X-RL-Daily-Remaining";
    private const string DailyResetHeader = "X-RL-Daily-Reset";

    // Without a reset time, a refusal for the rate limit holds requests back this long.
    private static readonly TimeSpan DefaultHoldBack = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private NexusRateLimit? _current;

    /// <summary>
    /// What the last response reported, or null before the first one.
    /// </summary>
    public NexusRateLimit? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// When requests may go out again, if both allowances are used up and neither has reset yet.
    /// </summary>
    public DateTimeOffset? HeldBackUntil()
    {
        NexusRateLimit? current = Current;
        if (current is not { HourlyRemaining: <= 0, DailyRemaining: <= 0 })
        {
            return null;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset nextReset = current.HourlyReset < current.DailyReset ? current.HourlyReset : current.DailyReset;
        return nextReset > now ? nextReset : null;
    }

    /// <summary>
    /// Takes the rate limit from a response's headers. A response without all of them changes nothing.
    /// </summary>
    public void Update(HttpResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (TryReadInt(headers, HourlyLimitHeader, out int hourlyLimit)
            && TryReadInt(headers, HourlyRemainingHeader, out int hourlyRemaining)
            && TryReadTime(headers, HourlyResetHeader, out DateTimeOffset hourlyReset)
            && TryReadInt(headers, DailyLimitHeader, out int dailyLimit)
            && TryReadInt(headers, DailyRemainingHeader, out int dailyRemaining)
            && TryReadTime(headers, DailyResetHeader, out DateTimeOffset dailyReset))
        {
            Set(new NexusRateLimit(hourlyLimit, hourlyRemaining, hourlyReset, dailyLimit, dailyRemaining, dailyReset));
        }
    }

    /// <summary>
    /// The API refused a request for the rate limit without headers saying until when: holds requests back as long
    /// as its <c>Retry-After</c> says, or for an hour.
    /// </summary>
    public void MarkUsedUp(RetryConditionHeaderValue? retryAfter)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset until = retryAfter switch
        {
            { Delta: { } delay } => now + delay,
            { Date: { } date } when date > now => date,
            _ => now + DefaultHoldBack,
        };
        NexusRateLimit? current = Current;
        Set(new NexusRateLimit(current?.HourlyLimit ?? 0, 0, until, current?.DailyLimit ?? 0, 0, until));
    }

    private static bool TryReadInt(HttpResponseHeaders headers, string name, out int value)
    {
        value = 0;
        return headers.TryGetValues(name, out IEnumerable<string>? values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadTime(HttpResponseHeaders headers, string name, out DateTimeOffset value)
    {
        value = default;
        return headers.TryGetValues(name, out IEnumerable<string>? values)
            && DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private void Set(NexusRateLimit limit)
    {
        lock (_gate)
        {
            _current = limit;
        }
    }
}
