using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.ModPages;

namespace ModShelf.Presentation.Common;

/// <summary>
/// What an update that replaces none of a mod's files is called, on its page and in the Library.
/// </summary>
internal static class NewFiles
{
    /// <summary>
    /// "New optional file"; or "New file" for a main file that replaces none of a mod with files, as a site whose files
    /// sit side by side offers one; null for an update that replaces a file, or of a mod that's only watched or has only
    /// archived versions, whose new main files are its updates.
    /// </summary>
    public static string? Note(LibraryUpdate update, LibraryMod mod)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mod);

        return update.Replaces is not null ? null
            : update.Category == ModFileCategory.Optional ? Strings.ModPageNewOptionalFile
            : mod.Files.Any(file => !OwnFileViewModel.IsInArchive(file.RelativePath)) ? Strings.ModPageNewFile
            : null;
    }
}
