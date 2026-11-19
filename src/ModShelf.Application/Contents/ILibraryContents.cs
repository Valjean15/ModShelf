using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Contents;

/// <summary>
/// Shows what the library's archives hold: their folders and files, and their text files, such as a readme, read where
/// they are. Nothing is ever extracted or written, so ModShelf stays a file manager (ADR 0106).
/// </summary>
public interface ILibraryContents
{
    /// <summary>
    /// The most of a text file that's read: its first 256 KB.
    /// </summary>
    const int MaxTextBytes = 256 * 1024;

    /// <summary>
    /// Whether a file of this name is an archive whose contents can be shown: a .zip, .7z or .rar.
    /// </summary>
    bool CanList(string fileName);

    /// <summary>
    /// The library's mod whose page <paramref name="link"/> is, or is on, found without asking its site, for its files.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The library has no mod at that link.</exception>
    Task<LibraryMod> FindModAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Lists what the library's <paramref name="file"/> holds.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The library doesn't have the file.</exception>
    /// <exception cref="FileNotFoundException">The file isn't where the library says it is.</exception>
    /// <exception cref="ArchiveUnreadableException">
    /// The file isn't an archive ModShelf reads, or needs a password, or is damaged.
    /// </exception>
    /// <exception cref="IOException">The file couldn't be read, such as when another program holds it.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read the file.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library's folder isn't known.</exception>
    Task<ArchiveContents> ListAsync(ModFileRef file, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the text file at <paramref name="entry"/> in the library's <paramref name="file"/>, up to
    /// <see cref="MaxTextBytes"/>: as UTF-8, or UTF-16 with its byte order mark, or else as Latin-1.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The library doesn't have the file, or the archive has no such file in it.</exception>
    /// <exception cref="FileNotFoundException">The file isn't where the library says it is.</exception>
    /// <exception cref="ArchiveUnreadableException">
    /// The entry isn't text, or needs a password, or the archive isn't one ModShelf reads or is damaged.
    /// </exception>
    /// <exception cref="IOException">The archive couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read the archive.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library's folder isn't known.</exception>
    Task<ArchiveText> ReadTextAsync(ModFileRef file, string entry, CancellationToken cancellationToken);
}
