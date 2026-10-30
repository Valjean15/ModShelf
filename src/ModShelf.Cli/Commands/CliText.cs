using System.Globalization;

namespace ModShelf.Cli.Commands;

/// <summary>
/// How the commands write times, dates and counts.
/// </summary>
internal static class CliText
{
    /// <summary>
    /// A moment in local time, to the minute, such as <c>2026-10-29 21:05</c>.
    /// </summary>
    public static string Time(DateTimeOffset moment) => moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A moment's day in local time, such as <c>2026-10-29</c>.
    /// </summary>
    public static string Date(DateTimeOffset moment) => moment.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A count, with the user's thousands separator.
    /// </summary>
    public static string Count(long count) => count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// A count of what it counts, such as "1 file" or "1,204 files".
    /// </summary>
    public static string Count(int count, string one, string many)
        => count == 1 ? $"1 {one}" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} {many}");
}
