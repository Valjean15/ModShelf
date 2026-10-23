using System.Globalization;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Formats byte counts for people.
/// </summary>
internal static class Sizes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Such as "512 B", "1.5 MB" or "1.00 GB", in powers of 1024 as file managers show them.
    /// </summary>
    public static string Format(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string format = unit == 0 ? "0" : value < 10 ? "0.00" : value < 100 ? "0.0" : "0";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    /// <summary>
    /// Such as "45%", or the byte count alone when the total isn't known.
    /// </summary>
    public static string Progress(long bytes, long? total)
        => total is > 0
            ? $"{(100.0 * bytes / total.Value).ToString("0", CultureInfo.CurrentCulture)}% ({Format(bytes)} of {Format(total.Value)})"
            : Format(bytes);
}
