using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// Where a mod's files are in the library, and the names they're saved under: the rules downloading into the library
/// places files by, and rescanning looks for them by.
/// </summary>
internal static class LibraryLocations
{
    /// <summary>
    /// The name a file gets when its own is taken: with the file's ID before its extension, as <c>SkyUI (35407).7z</c>.
    /// </summary>
    public static string WithId(string name, string fileId)
        => LibraryNames.File($"{Path.GetFileNameWithoutExtension(name)} ({fileId}){Path.GetExtension(name)}");

    /// <summary>
    /// The folder part of a library path, with no ending separator; empty for a file at the root.
    /// </summary>
    public static string FolderOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf(LibraryPaths.Separator);
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    /// <summary>
    /// The folder of a mod with a file at <paramref name="relativePath"/>: the file's folder, or the one holding its
    /// <c>_archive</c> folder.
    /// </summary>
    public static string ModFolderOf(string relativePath)
        => OldVersions.IsArchived(relativePath) ? FolderOf(FolderOf(relativePath)) : FolderOf(relativePath);

    /// <summary>
    /// The last part of a library path: the file's name.
    /// </summary>
    public static string NameOf(string relativePath) => relativePath[(relativePath.LastIndexOf(LibraryPaths.Separator) + 1)..];
}
