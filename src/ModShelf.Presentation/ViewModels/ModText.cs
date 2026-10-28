using System.Globalization;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// How the Browse screen writes what a site says of a mod or a file: its dates and its counts.
/// </summary>
internal static class ModText
{
    /// <summary>
    /// A date as the user reads it: in their time zone, written as their culture writes short dates.
    /// </summary>
    public static string LocalDate(DateTimeOffset moment, TimeProvider timeProvider)
        => TimeZoneInfo.ConvertTime(moment, timeProvider.LocalTimeZone).ToString("d", CultureInfo.CurrentCulture);

    /// <summary>
    /// A count the site keeps, such as "1 download" or "2,134 downloads"; empty when it keeps none.
    /// </summary>
    public static string Count(long? count, string one, string many) => count switch
    {
        null => string.Empty,
        1 => one,
        long value => Text.Format(many, value),
    };
}
