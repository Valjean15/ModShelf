using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// What a rescan did, and what it found.
/// </summary>
/// <param name="Mods">How many mods it rescanned.</param>
/// <param name="Added">The files it found on disk and matched, now in the library.</param>
/// <param name="Hashed">The library's files it read the SHA-256 of.</param>
/// <param name="Missing">The library's files that aren't on disk.</param>
/// <param name="Unmatched">The files in the mods' folders that match none of their mods' files, by full path.</param>
/// <param name="Failures">The mods whose files couldn't be matched, because their sites couldn't be asked.</param>
public sealed record LibraryRescan(
    int Mods,
    IReadOnlyList<LibraryFile> Added,
    IReadOnlyList<LibraryFile> Hashed,
    IReadOnlyList<LibraryFile> Missing,
    IReadOnlyList<string> Unmatched,
    IReadOnlyList<RescanFailure> Failures);
