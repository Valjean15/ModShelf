using System.Text.RegularExpressions;

using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A package's <c>CHANGELOG.md</c>, which its author writes as they like, read as ModShelf's changelogs: each heading
/// that names a version, such as <c>## 1.4.66</c> or <c># v1.2.0 (2026-11-02)</c>, starts that version's changes, and each
/// line under it, a list's markers taken off, is one, under subheadings such as <c>### Fixed</c> too. Lines before the
/// first such heading aren't any version's.
/// </summary>
internal static partial class ThunderstoreChangelogs
{
    /// <returns>Each version's changes, in the order the file gives them; empty when no heading names a version.</returns>
    public static IReadOnlyList<ModChangelog> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        List<(string Version, List<string> Changes)> versions = [];
        List<string>? current = null;
        foreach (string raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (Heading().Match(line) is { Success: true } heading)
            {
                Match version = Version().Match(heading.Groups["text"].Value);
                if (version.Success && versions.All(listed => listed.Version != version.Value))
                {
                    current = [];
                    versions.Add((version.Value, current));
                }
                else if (version.Success)
                {
                    // A version named twice: its first section is it.
                    current = null;
                }

                // A heading that names no version, such as "### Fixed", leaves its lines in the version above it.

                continue;
            }

            if (current is not null && Marker().Replace(line, string.Empty).Trim() is { Length: > 0 } change)
            {
                current.Add(change);
            }
        }

        return [.. versions.Select(version => new ModChangelog(version.Version, version.Changes))];
    }

    [GeneratedRegex(@"^#{1,6}\s*(?<text>.*?)\s*#*$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?<![\d.])\d+\.\d+\.\d+(?![\d.]*\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Version();

    // "- ", "* ", "+ " and "1. ", which start a list's items.
    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
