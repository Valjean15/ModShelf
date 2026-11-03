using System.Globalization;

using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Writes a moment as the user reads it: in their time zone, as their culture writes dates and times.
/// </summary>
internal static class Dates
{
    /// <summary>
    /// The short date, such as "10/29/2026".
    /// </summary>
    public static string Day(DateTimeOffset moment, TimeProvider timeProvider) => Write(moment, timeProvider, "d");

    /// <summary>
    /// The short date and time, such as "10/29/2026 4:40 PM".
    /// </summary>
    public static string DayAndTime(DateTimeOffset moment, TimeProvider timeProvider) => Write(moment, timeProvider, "g");

    /// <summary>
    /// How long until something, to the nearest minute, such as "in 4 hours" or "in 25 minutes": a whole hour or day
    /// starts at its first minute, so 5 hours and 59.5 minutes is "in 6 hours". "In a moment" for less than half a minute,
    /// or for something due already.
    /// </summary>
    public static string In(TimeSpan wait)
    {
        TimeSpan rounded = TimeSpan.FromMinutes(Math.Round(wait.TotalMinutes, MidpointRounding.AwayFromZero));
        return rounded switch
        {
            { TotalMinutes: < 1 } => Strings.InAMoment,
            { TotalHours: < 1 } => Counts.Of((long)rounded.TotalMinutes, Strings.InOneMinute, Strings.InMinutes),
            { TotalDays: < 1 } => Counts.Of((long)rounded.TotalHours, Strings.InOneHour, Strings.InHours),
            _ => Counts.Of((long)rounded.TotalDays, Strings.InOneDay, Strings.InDays),
        };
    }

    /// <summary>
    /// How long ago, such as "2 hours ago" or "yesterday", up to 30 days; the short date before that, or for a moment
    /// a minute or more to come, which a clock set back gives. A moment less than a minute to come, which clocks a little
    /// apart give, is "just now".
    /// </summary>
    public static string Ago(DateTimeOffset moment, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan age = timeProvider.GetUtcNow() - moment;
        return age switch
        {
            { TotalMinutes: <= -1 } => Day(moment, timeProvider),
            { TotalMinutes: < 1 } => Strings.AgoJustNow,
            { TotalHours: < 1 } => Counts.Of((long)age.TotalMinutes, Strings.AgoOneMinute, Strings.AgoMinutes),
            { TotalDays: < 1 } => Counts.Of((long)age.TotalHours, Strings.AgoOneHour, Strings.AgoHours),
            { TotalDays: <= 30 } => Counts.Of((long)age.TotalDays, Strings.AgoOneDay, Strings.AgoDays),
            _ => Day(moment, timeProvider),
        };
    }

    /// <summary>
    /// The day of a moment in the user's time zone.
    /// </summary>
    public static DateOnly LocalDay(DateTimeOffset moment, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeProvider.LocalTimeZone).DateTime);
    }

    /// <summary>
    /// A day as a heading: "Today", "Yesterday", or the long date, such as "Monday, November 2, 2026".
    /// </summary>
    public static string DayHeading(DateOnly day, TimeProvider timeProvider)
    {
        DateOnly today = LocalDay(timeProvider.GetUtcNow(), timeProvider);
        return day == today ? Strings.DayToday
            : day == today.AddDays(-1) ? Strings.DayYesterday
            : day.ToString("D", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The short time, such as "4:40 PM".
    /// </summary>
    public static string Time(DateTimeOffset moment, TimeProvider timeProvider) => Write(moment, timeProvider, "t");

    private static string Write(DateTimeOffset moment, TimeProvider timeProvider, string format)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TimeZoneInfo.ConvertTime(moment, timeProvider.LocalTimeZone).ToString(format, CultureInfo.CurrentCulture);
    }
}
