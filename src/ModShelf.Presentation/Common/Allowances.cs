using System.Globalization;

using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Common;

/// <summary>
/// A site's allowances of requests, as the screens say them: the tightest one still running, what it is, and when it
/// renews, in the user's time.
/// </summary>
internal static class Allowances
{
    /// <summary>
    /// The allowance with the fewest requests left of those that haven't renewed yet; null when every one has.
    /// </summary>
    public static QuotaAllowance? Tightest(ApiQuota quota, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quota);

        return quota.Allowances
            .Where(allowance => allowance.ResetsAt > now)
            .OrderBy(allowance => allowance.Remaining)
            .ThenBy(allowance => allowance.ResetsAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Of the allowances that haven't renewed yet, the one with the smallest share of its requests left, when that's below
    /// <paramref name="percent"/>; null when none is.
    /// </summary>
    public static QuotaAllowance? Low(ApiQuota quota, DateTimeOffset now, int percent)
    {
        ArgumentNullException.ThrowIfNull(quota);

        return quota.Allowances
            .Where(allowance => allowance.ResetsAt > now && IsBelow(allowance, percent))
            .OrderBy(allowance => (double)allowance.Remaining / allowance.Limit)
            .ThenBy(allowance => allowance.ResetsAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether the allowance has fewer than <paramref name="percent"/> of its requests left.
    /// </summary>
    public static bool IsBelow(QuotaAllowance allowance, int percent)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        return allowance.Limit > 0 && allowance.Remaining * 100L < (long)percent * allowance.Limit;
    }

    /// <summary>
    /// <paramref name="hour"/> for an hour's allowance, <paramref name="day"/> for a day's, and <paramref name="other"/>
    /// for another.
    /// </summary>
    public static string Of(QuotaAllowance allowance, string hour, string day, string other)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        return allowance.Period == TimeSpan.FromHours(1) ? hour
            : allowance.Period == TimeSpan.FromDays(1) ? day
            : other;
    }

    /// <summary>
    /// When the allowance renews, in the user's time: its time today, such as "4:00 PM", or its day and time on another
    /// day, such as "Sat 9:00 AM".
    /// </summary>
    public static string RenewsAt(QuotaAllowance allowance, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(allowance);
        ArgumentNullException.ThrowIfNull(time);

        DateTimeOffset at = TimeZoneInfo.ConvertTime(allowance.ResetsAt, time.LocalTimeZone);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone);
        return at.Date == now.Date
            ? at.ToString("t", CultureInfo.CurrentCulture)
            : at.ToString("ddd ", CultureInfo.CurrentCulture) + at.ToString("t", CultureInfo.CurrentCulture);
    }
}
