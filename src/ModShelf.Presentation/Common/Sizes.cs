using System.Globalization;

using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Sizes of files for people to read.
/// </summary>
internal static class Sizes
{
    private const int LargestUnit = 4;

    /// <summary>
    /// Such as "512 B", "1.50 MB" or "45.6 MB", in powers of 1024 as file managers show them.
    /// </summary>
    public static string Format(long bytes)
    {
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < LargestUnit)
        {
            value /= 1024;
            unit++;
        }

        string format = unit == 0 ? "0" : value < 10 ? "0.00" : value < 100 ? "0.0" : "0";
        return Text.Format(UnitFormat(unit), value.ToString(format, CultureInfo.CurrentCulture));
    }

    // Read when used, so the text follows the culture the app runs in.
    private static string UnitFormat(int unit) => unit switch
    {
        0 => Strings.SizeBytes,
        1 => Strings.SizeKilobytes,
        2 => Strings.SizeMegabytes,
        3 => Strings.SizeGigabytes,
        _ => Strings.SizeTerabytes,
    };
}
