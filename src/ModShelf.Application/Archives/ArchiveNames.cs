using System.Globalization;
using System.Text.RegularExpressions;

namespace ModShelf.Application.Archives;

/// <summary>
/// What an archive's name says of it: the archives Nexus Mods hands out are named after the file, with the mod's ID, its
/// version and when it was uploaded, as <c>SkyUI_5_2_SE-12604-5-2SE-1573160372.7z</c> (ADR 0101).
/// </summary>
internal static partial class ArchiveNames
{
    /// <summary>
    /// The extensions of the archives mods come in. Anything else in a folder, such as a readme or a mod manager's note,
    /// isn't imported.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".7z", ".zip", ".rar"];

    /// <summary>
    /// Whether a file is an archive, by its extension in any case.
    /// </summary>
    public static bool IsArchive(string fileName)
        => Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The mod's ID in a name such as Nexus Mods gives its archives: the first number between dashes, before the
    /// upload's time at the end. Mod Organizer 2's " (1)" for a second download of a name is passed over.
    /// </summary>
    /// <returns>The ID; null for any other name.</returns>
    public static string? ModIdOf(string fileName)
    {
        Match match = NexusName().Match(Path.GetFileName(fileName));
        return match.Success
            && long.TryParse(match.Groups["mod"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out long id)
            && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    [GeneratedRegex(@"^.+?-(?<mod>\d{1,9})(?:-.*?)?-\d{9,11}(?:\s*\(\d+\))?\.[A-Za-z0-9]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NexusName();
}
