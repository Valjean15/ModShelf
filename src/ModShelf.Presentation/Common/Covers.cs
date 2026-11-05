using System.Globalization;

namespace ModShelf.Presentation.Common;

/// <summary>
/// A game's cover, or an account's picture, until it has a picture of its own: its initial on a hue of its own.
/// </summary>
internal static class Covers
{
    /// <summary>
    /// A hue from 0 to 359 from the game's key, the same in every run, which <see cref="string.GetHashCode()"/> isn't.
    /// </summary>
    public static int HueOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        uint hash = 2166136261;
        foreach (char letter in key)
        {
            hash = (hash ^ letter) * 16777619;
        }

        return (int)(hash % 360);
    }

    /// <summary>
    /// The first letter of the game's name, as a capital.
    /// </summary>
    public static string InitialOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.CurrentCulture).ToString() : string.Empty;
    }
}
