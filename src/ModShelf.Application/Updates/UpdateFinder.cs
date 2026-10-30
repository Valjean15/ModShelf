using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Updates;

/// <summary>
/// Works out a library mod's updates from its files as its site lists them. Version labels are never compared.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// A library file's update is the newest file of its update chain (<see cref="ModFile.ReplacedBy"/>, as far as
/// <see cref="Replacements"/> believes it), when the library doesn't have it. The update replaces the newest library file
/// on the way. An archived file, one its author withdrew, is no update, though the chain may go on past it to one.
/// </item>
/// <item>
/// A main file in no chain, since authors don't always link updates, has the newest main file in no chain that the site
/// uploaded after it, when the library doesn't have that. Files in chains belong to other lines of files.
/// </item>
/// <item>
/// A mod that's only watched, without files other than archived ones, has the main files uploaded since it was last
/// checked, and keeps those found before while the site still lists them.
/// </item>
/// </list>
/// </remarks>
internal static class UpdateFinder
{
    /// <param name="mod">The mod as the library has it.</param>
    /// <param name="siteFiles">The mod's files as its site lists them now.</param>
    /// <param name="since">For a mod that's only watched: when it was last checked, or added, less the margin for clocks.</param>
    /// <param name="foundAt">When this check started.</param>
    /// <returns>The mod's updates, the newest first.</returns>
    public static IReadOnlyList<LibraryUpdate> Find(LibraryMod mod, IReadOnlyList<ModFile> siteFiles, DateTimeOffset since, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(siteFiles);

        Dictionary<ModFileRef, ModFile> listed = [];
        foreach (ModFile file in siteFiles)
        {
            listed[file.Reference] = file;
        }

        HashSet<ModFileRef> have = [.. mod.Files.Select(file => file.File)];
        HashSet<ModFileRef> chained =
        [
            .. siteFiles
                .Select(file => (file.Reference, Next: Replacements.Next(file, listed)))
                .Where(link => link.Next is not null)
                .SelectMany(link => new[] { link.Reference, link.Next! }),
        ];
        LibraryFile[] current = [.. mod.Files.Where(file => !OldVersions.IsArchived(file.RelativePath))];

        // Each newer file and what it replaces. Library files are taken oldest first, so when two lead to the same newer
        // file, it replaces the newer of them. A chain's files and a main file in no chain never lead to the same one.
        Dictionary<ModFileRef, (ModFile File, ModFileRef? Replaces)> found = [];
        void Add(ModFile file, ModFileRef? replaces) => found[file.Reference] = (file, replaces);

        if (current.Length == 0)
        {
            foreach (ModFile file in siteFiles.Where(file => file.Category == ModFileCategory.Main && file.UploadedAt > since && !have.Contains(file.Reference)))
            {
                Add(file, null);
            }

            foreach (LibraryUpdate earlier in mod.Updates.Where(update => update.Replaces is null && !have.Contains(update.File)))
            {
                if (listed.TryGetValue(earlier.File, out ModFile? file))
                {
                    Add(file, null);
                }
            }
        }

        foreach (LibraryFile file in current.OrderBy(file => file.UploadedAt))
        {
            (ModFileRef newest, ModFileRef newestHad) = EndOfChain(file.File, listed, have);
            if (newest != file.File)
            {
                if (!have.Contains(newest) && listed.TryGetValue(newest, out ModFile? update))
                {
                    Add(update, newestHad);
                }
            }
            else if (file.Category == ModFileCategory.Main
                && !chained.Contains(file.File)
                && siteFiles
                    .Where(other => other.Category == ModFileCategory.Main
                        && other.UploadedAt > file.UploadedAt
                        && !have.Contains(other.Reference)
                        && !chained.Contains(other.Reference))
                    .MaxBy(other => other.UploadedAt) is { } newer)
            {
                Add(newer, file.File);
            }
        }

        return
        [
            .. found.Values
                .OrderByDescending(update => update.File.UploadedAt)
                .Select(update => new LibraryUpdate(
                    update.File.Reference,
                    update.File.Name,
                    update.File.FileName,
                    update.File.VersionLabel,
                    update.File.Category,
                    update.File.UploadedAt,
                    update.Replaces,
                    foundAt,
                    update.File.PageUrl)),
        ];
    }

    /// <summary>
    /// The newest file of <paramref name="file"/>'s update chain that isn't archived, or that the library has, and the
    /// newest file on the way that the library has. A chain that goes round ends where it would.
    /// </summary>
    private static (ModFileRef Newest, ModFileRef NewestHad) EndOfChain(ModFileRef file, Dictionary<ModFileRef, ModFile> listed, HashSet<ModFileRef> have)
    {
        HashSet<ModFileRef> seen = [file];
        ModFileRef current = file;
        ModFileRef newest = file;
        ModFileRef newestHad = file;
        while (listed.TryGetValue(current, out ModFile? site) && Replacements.Next(site, listed) is { } next && seen.Add(next))
        {
            current = next;
            if (have.Contains(current))
            {
                // Nothing before it is newer than what the library has.
                newest = current;
                newestHad = current;
            }
            else if (!listed.TryGetValue(current, out ModFile? reached) || reached.Category != ModFileCategory.Archived)
            {
                newest = current;
            }
        }

        return (newest, newestHad);
    }
}
