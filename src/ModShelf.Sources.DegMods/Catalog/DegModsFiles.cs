using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// A mod's files as ModShelf lists them: the first the author listed is its main one, the others optional; each made
/// when the mod's event was, since DEG Mods doesn't date files; each with its SHA-256 when the author gave it, and
/// ModShelf's link to it.
/// </summary>
/// <remarks>
/// An event lists a mod's files as they are now: one the author replaced is gone. So the main file uploaded again is the
/// update of the one the library has, as on Nexus Mods, and the optional ones new since a check are new files.
/// </remarks>
internal static class DegModsFiles
{
    public static IReadOnlyList<ModFile> Files(DegMod mod, ModRef modRef, Uri site)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(modRef);
        ArgumentNullException.ThrowIfNull(site);

        return
        [
            .. mod.Downloads.Select((download, index) => new ModFile(
                new ModFileRef(modRef, download.Id),
                Name(mod, download, index),
                FileName(mod, download),
                download.Version,
                index == 0 ? ModFileCategory.Main : ModFileCategory.Optional,
                sizeInBytes: null,
                mod.UpdatedAt,
                download.Sha256 is { } sha256 ? [new FileHash(FileHashAlgorithm.Sha256, sha256)] : [],
                DegModsLink.FilePage(site, mod.Kind, mod.Author, mod.D, download.Id))),
        ];
    }

    /// <summary>
    /// What the file is called: the author's title for it, or its name, or the mod's.
    /// </summary>
    public static string Name(DegMod mod, DegDownload download, int index)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(download);

        return download.Title ?? download.FileName ?? (index == 0 ? mod.Title : $"{mod.Title} ({index + 1})");
    }

    /// <summary>
    /// The name a file is saved as, made to work on every system as the library's names are: the name the author gave
    /// it, or the one in its link; a file server's link names a file by its SHA-256, so its name is the file's title with
    /// the link's extension.
    /// </summary>
    public static string FileName(DegMod mod, DegDownload download)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(download);

        if (download.FileName is { } given && Usable(given))
        {
            return LibraryNames.File(given);
        }

        string last = Uri.UnescapeDataString(download.Url.Segments[^1].TrimEnd('/'));
        string stem = Path.GetFileNameWithoutExtension(last);
        string extension = Path.GetExtension(last);
        bool named = extension.Length > 1 && !(stem.Length == 64 && ModEvents.IsHex(stem));
        string name = named ? last : (download.Title ?? mod.Title) + (extension.Length > 1 ? extension : string.Empty);
        return Usable(name) ? LibraryNames.File(name) : download.Id;
    }

    /// <summary>
    /// Whether a name is more than dots and spaces.
    /// </summary>
    private static bool Usable(string name) => !string.IsNullOrWhiteSpace(name.Replace('.', ' '));
}
