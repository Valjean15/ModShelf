using System.Globalization;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Fills in the text the resources hold, such as "Downloading {0} into {1}.", for the culture the app runs in.
/// </summary>
internal static class Text
{
    public static string Format(string format, params object?[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
