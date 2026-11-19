using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// The folders a mod's files are looked for in, by rescanning the library and by freeing up space (ADR 0045,
/// ADR 0104).
/// </summary>
internal static class ModFolders
{
    /// <summary>
    /// The library's folders of the mod, and where its game's pattern puts its new files, each with its <c>_archive</c>,
    /// as absolute paths.
    /// </summary>
    /// <param name="stored">The mod as the library has it; null when it isn't in the library.</param>
    /// <param name="name">The mod's name, which the pattern may use.</param>
    public static IEnumerable<string> Of(LibraryRoots roots, AppSettings settings, ModRef mod, LibraryMod? stored, string name)
    {
        string byPattern = LibraryLocations.FolderOf(
            settings.FoldersFor(mod.Game).Render(mod.Game.Source.Value, mod.Game.Key, name, mod.Id, "file"));
        return (stored?.Files.Select(file => LibraryLocations.ModFolderOf(file.RelativePath)) ?? [])
            .Append(byPattern)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(folder => new[] { folder, folder.Length == 0 ? OldVersions.ArchiveFolder : $"{folder}{LibraryPaths.Separator}{OldVersions.ArchiveFolder}" })
            .Select(folder => folder.Length == 0 ? roots.For(mod.Game) : roots.Resolve(mod.Game, folder));
    }
}
