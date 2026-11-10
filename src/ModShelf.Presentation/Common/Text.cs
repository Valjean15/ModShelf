using System.Globalization;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Fills in the text the resources hold, such as "Downloading {0} into {1}.", for the culture the app runs in.
/// </summary>
internal static class Text
{
    public static string Format(string format, params object?[] values) => string.Format(CultureInfo.CurrentCulture, format, values);

    /// <summary>
    /// A word as it starts a sentence or a label, such as a site's "track" as "Track".
    /// </summary>
    public static string Capitalized(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..];
    }

    /// <summary>
    /// Names in a sentence, in the order given: "Nexus Mods", "Nexus Mods and GameBanana", or "A, B and C".
    /// </summary>
    public static string List(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        string[] listed = [.. names];
        return listed.Length switch
        {
            0 => string.Empty,
            1 => listed[0],
            _ => Format(Resources.Strings.ListAnd, string.Join(", ", listed[..^1]), listed[^1]),
        };
    }
}
