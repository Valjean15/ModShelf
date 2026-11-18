using ModShelf.Domain.Mods;

namespace ModShelf.Application.Archives;

/// <summary>
/// Imports the archives of a folder, such as another mod manager's downloads, into the library (ADR 0101): each one
/// told to be a site's file, the cheapest way first, is copied in, where a download of it would go, and its mod watched.
/// The archives are only ever read: nothing in the folder is moved, changed or deleted, and nothing is installed.
/// </summary>
public interface IArchiveImport
{
    /// <summary>
    /// Lists the folder's archives, <c>.7z</c>, <c>.zip</c> and <c>.rar</c>, not its subfolders', with what the notes
    /// Mod Organizer 2 keeps beside them, or their names, say of their mods. Nothing is asked of any site.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// There's no such folder, or it's in the library, or the game's site can't list its mods' files, so nothing could be
    /// identified; the message says which.
    /// </exception>
    /// <exception cref="IOException">The folder couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read it.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read, so the library's folders aren't known.</exception>
    Task<ArchiveScan> ScanAsync(string folder, GameRef game, CancellationToken cancellationToken);

    /// <summary>
    /// Identifies the scanned archives. Each one is read once for its hashes: one the library has, by its SHA-256, is
    /// in the library already, without asking. Otherwise a note or a name is confirmed against the mod's files on its
    /// site, one request a mod; and what's left is looked up by its hash, one request an archive, when the site can. A
    /// mod new to the library is read once more, for its name. A refused account or a request to slow down stops
    /// asking the site: the archives left are unknown, saying why.
    /// </summary>
    /// <param name="progress">Told how many archives are done after each.</param>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    Task<ArchivePlan> IdentifyAsync(ArchiveScan scan, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Copies the identified archives into the library, one at a time, where a download of each would go, and watches
    /// the mods new to it; a mod the library has keeps its own watching. Each file is recorded as found, with its
    /// SHA-256. One the library has by then is passed over, so an import stopped halfway goes on where it was when run
    /// again. One that can't be copied, such as for want of room, doesn't stop the others: the result says why. No site
    /// is asked.
    /// </summary>
    /// <param name="progress">Told how many of the archives to import are done after each.</param>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    /// <exception cref="OperationCanceledException">Stopped; what was copied before stays in the library.</exception>
    Task<ArchiveImportResult> ImportAsync(ArchivePlan plan, IProgress<int>? progress, CancellationToken cancellationToken);
}
