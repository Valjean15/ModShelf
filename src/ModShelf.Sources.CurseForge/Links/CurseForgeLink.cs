using System.Globalization;
using System.Text.RegularExpressions;

namespace ModShelf.Sources.CurseForge.Links;

/// <summary>
/// A link to a mod or one of its files on CurseForge's website: <c>https://www.curseforge.com/&lt;game&gt;/&lt;class&gt;/&lt;mod&gt;</c>,
/// with <c>/files/&lt;file&gt;</c> or <c>/download/&lt;file&gt;</c> after it for a file. Each part before the file is a
/// slug, such as <c>minecraft/mc-mods/modmenu</c>; the file is CurseForge's number for it.
/// </summary>
internal sealed partial record CurseForgeLink(string Game, string Class, string Mod, long? File)
{
    private const string Host = "curseforge.com";

    /// <summary>
    /// The link's parts; null for a link that isn't to a mod on CurseForge's website.
    /// </summary>
    /// <exception cref="ArgumentException">It's a link to a mod's file, but not to one file, such as one whose number is missing.</exception>
    public static CurseForgeLink? Parse(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!link.IsAbsoluteUri
            || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp)
            || !(string.Equals(link.Host, Host, StringComparison.OrdinalIgnoreCase) || string.Equals(link.Host, "www." + Host, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string[] parts = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts.Take(3).All(part => Slug().IsMatch(part)))
        {
            return null;
        }

        CurseForgeLink mod = new(parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant(), parts[2].ToLowerInvariant(), null);
        if (parts.Length == 3 || (parts.Length == 4 && parts[3] is "files" or "description" or "relations" or "images"))
        {
            return mod;
        }

        if (parts[3] is "files" or "download")
        {
            return parts.Length >= 5 && long.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out long file) && file > 0
                ? mod with { File = file }
                : throw new ArgumentException($"{link} is a link to a file of a mod on CurseForge, but names no file.", nameof(link));
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$")]
    private static partial Regex Slug();
}
