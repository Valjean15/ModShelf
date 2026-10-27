using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Finds a library file's old versions, by the site's word on which file replaced which, and decides what a rule does
/// with them.
/// </summary>
internal static class OldVersions
{
    /// <summary>
    /// The folder old versions move into, inside their own folder.
    /// </summary>
    public const string ArchiveFolder = "_archive";

    /// <summary>
    /// The mod's library files that <paramref name="newer"/> replaced, directly or through versions between them, the
    /// nearest first. A file the site no longer lists, or whose chain doesn't reach <paramref name="newer"/>, isn't one.
    /// </summary>
    /// <param name="siteFiles">The mod's files as its site lists them, with which replaced which.</param>
    public static IReadOnlyList<LibraryFile> ReplacedBy(LibraryMod mod, ModFileRef newer, IReadOnlyList<ModFile> siteFiles)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(siteFiles);

        Dictionary<ModFileRef, ModFile> listed = [];
        foreach (ModFile file in siteFiles)
        {
            listed[file.Reference] = file;
        }

        List<(LibraryFile File, int Distance)> older = [];
        foreach (LibraryFile file in mod.Files.Where(file => file.File != newer))
        {
            if (StepsTo(file.File, newer, listed) is { } steps)
            {
                older.Add((file, steps));
            }
        }

        return [.. older.OrderBy(version => version.Distance).ThenByDescending(version => version.File.UploadedAt).Select(version => version.File)];
    }

    /// <summary>
    /// What <paramref name="rule"/> does with the old versions of <paramref name="newer"/>, a file the mod has or is
    /// getting. Old versions already in an <c>_archive</c> folder aren't archived again.
    /// </summary>
    public static OldVersionPlan Plan(LibraryMod mod, ModFileRef newer, IReadOnlyList<ModFile> siteFiles, OldVersionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        IReadOnlyList<LibraryFile> older = ReplacedBy(mod, newer, siteFiles);
        return rule.Action switch
        {
            OldVersionAction.Archive => new OldVersionPlan([.. older.Where(file => !IsArchived(file.RelativePath))], []),

            // The newer file counts as the first version kept.
            OldVersionAction.KeepLatest => new OldVersionPlan([], [.. older.Skip(rule.Count - 1)]),
            _ => OldVersionPlan.None,
        };
    }

    /// <summary>
    /// Whether a library path is in an <c>_archive</c> folder.
    /// </summary>
    public static bool IsArchived(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string[] parts = relativePath.Split(LibraryPaths.Separator);
        return parts.Length > 1 && string.Equals(parts[^2], ArchiveFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where a file goes when it's archived: into an <c>_archive</c> folder in its folder, under <paramref name="name"/>.
    /// </summary>
    public static string ArchivePath(string relativePath, string name)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(name);

        int separator = relativePath.LastIndexOf(LibraryPaths.Separator);
        string folder = separator < 0 ? ArchiveFolder : $"{relativePath[..separator]}{LibraryPaths.Separator}{ArchiveFolder}";
        return $"{folder}{LibraryPaths.Separator}{name}";
    }

    /// <summary>
    /// How many replacements lead from <paramref name="file"/> to <paramref name="newer"/>; or null when none do.
    /// </summary>
    private static int? StepsTo(ModFileRef file, ModFileRef newer, Dictionary<ModFileRef, ModFile> listed)
    {
        HashSet<ModFileRef> seen = [];
        ModFileRef current = file;
        for (int steps = 1; seen.Add(current) && listed.TryGetValue(current, out ModFile? site) && site.ReplacedBy is { } next; steps++)
        {
            if (next == newer)
            {
                return steps;
            }

            current = next;
        }

        return null;
    }
}
