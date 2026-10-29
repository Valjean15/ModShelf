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

    // Without a reset time, a refusal for the rate limit holds requests back this long. It's also the longest any hold
    // lasts, whatever the reset times say: the hourly allowance resets within it, and reset times far ahead mean a
    // clock that runs behind the API's.
    private static readonly TimeSpan LongestHoldBack = TimeSpan.FromHours(1);

    // A refusal whose headers show allowance left came from something that limits bursts, so it holds back this long
    // when it doesn't say.
    private static readonly TimeSpan BriefHoldBack = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private NexusRateLimit? _current;
    private DateTimeOffset _seenAt;
    private DateTimeOffset? _briefHold;

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
        NexusRateLimit? current;
        DateTimeOffset seenAt;
        DateTimeOffset? briefHold;
        lock (_gate)
        {
            current = _current;
            seenAt = _seenAt;
            briefHold = _briefHold;
        }

        DateTimeOffset? until = briefHold;
        if (current is { HourlyRemaining: <= 0, DailyRemaining: <= 0 })
        {
            DateTimeOffset nextReset = current.HourlyReset < current.DailyReset ? current.HourlyReset : current.DailyReset;
            DateTimeOffset latest = seenAt + LongestHoldBack;
            until = nextReset < latest ? nextReset : latest;
        }

        return until > timeProvider.GetUtcNow() ? until : null;
    }

    /// <summary>
    /// Takes the rate limit from a response's headers. A response without all of them changes nothing.
    /// </summary>
    /// <returns>Whether the response had them all.</returns>
    public bool Update(HttpResponseHeaders headers)
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
            return true;
        }

        return false;
    }

    /// <summary>
    /// The API refused a request for the rate limit without headers saying until when: holds requests back as long
    /// as its <c>Retry-After</c> says, or for an hour.
    /// </summary>
    public void MarkUsedUp(RetryConditionHeaderValue? retryAfter)
    {
        DateTimeOffset until = Until(retryAfter, LongestHoldBack);
        NexusRateLimit? current = Current;
        Set(new NexusRateLimit(current?.HourlyLimit ?? 0, 0, until, current?.DailyLimit ?? 0, 0, until));
    }

    /// <summary>
    /// The API refused a request for the rate limit while its headers show allowance left: holds requests back as long
    /// as its <c>Retry-After</c> says, or for a minute, and keeps the allowances it reported.
    /// </summary>
    public void HoldBriefly(RetryConditionHeaderValue? retryAfter)
    {
        DateTimeOffset until = Until(retryAfter, BriefHoldBack);
        lock (_gate)
        {
            _briefHold = until;
        }
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

    private DateTimeOffset Until(RetryConditionHeaderValue? retryAfter, TimeSpan otherwise)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return retryAfter switch
        {
            { Delta: { } delay } => now + delay,
            { Date: { } date } when date > now => date,
            _ => now + otherwise,
        };
    }

    private void Set(NexusRateLimit limit)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            _current = limit;
            _seenAt = now;
        }
    }
}
