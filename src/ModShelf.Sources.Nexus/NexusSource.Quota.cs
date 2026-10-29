using ModShelf.Sources.Abstractions.Quota;
using ModShelf.Sources.Nexus.Api;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The account's rate limit: a daily allowance, and an hourly one for once that's used up.
/// </summary>
public sealed partial class NexusSource : IApiQuota
{
    public ApiQuota? Quota => _quota.Current is { } limit
        ? new ApiQuota(
        [
            Allowance(TimeSpan.FromDays(1), limit.DailyLimit, limit.DailyRemaining, limit.DailyReset),
            Allowance(TimeSpan.FromHours(1), limit.HourlyLimit, limit.HourlyRemaining, limit.HourlyReset),
        ])
        : null;

    /// <summary>
    /// An allowance as the headers gave it, with a count below zero, which no allowance has, taken as none.
    /// </summary>
    private static QuotaAllowance Allowance(TimeSpan period, int limit, int remaining, DateTimeOffset resetsAt)
        => new(period, Math.Max(limit, 0), Math.Max(remaining, 0), resetsAt);
}
