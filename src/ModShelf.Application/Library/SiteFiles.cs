using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Whether a file on disk is one of a site's files, as rescanning the library and importing archives tell (ADR 0045,
/// ADR 0101).
/// </summary>
internal static class SiteFiles
{
    /// <summary>
    /// How far a file's size may be from the site's: a site may give it rounded to the kilobyte, as Nexus Mods does for
    /// some files.
    /// </summary>
    public const long SizeTolerance = 1024;

    /// <summary>
    /// Whether a file on disk of <paramref name="size"/> bytes, named <paramref name="name"/>, is the site's file, saved as
    /// a download saves it: under its own name, made safe, or with its ID added.
    /// </summary>
    public static bool Matches(string name, long? size, ModFile file)
    {
        string saved = LibraryNames.File(file.FileName);
        bool named = string.Equals(name, saved, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LibraryLocations.WithId(saved, file.Reference.Id), StringComparison.OrdinalIgnoreCase);
        return named && SizeFits(size, file);
    }

    /// <summary>
    /// Whether a file of <paramref name="size"/> bytes could be the site's file by its size: within
    /// <see cref="SizeTolerance"/>, or any size when the site gives none.
    /// </summary>
    public static bool SizeFits(long? size, ModFile file)
        => file.SizeInBytes is not { } listed || (size is { } found && Math.Abs(found - listed) <= SizeTolerance);
}
