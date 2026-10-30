using System.Globalization;

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
    /// The short time, such as "4:40 PM".
    /// </summary>
    public static string Time(DateTimeOffset moment, TimeProvider timeProvider) => Write(moment, timeProvider, "t");

    private static string Write(DateTimeOffset moment, TimeProvider timeProvider, string format)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TimeZoneInfo.ConvertTime(moment, timeProvider.LocalTimeZone).ToString(format, CultureInfo.CurrentCulture);
    }
}
