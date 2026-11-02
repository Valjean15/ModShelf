namespace ModShelf.Cli.Mcp;

/// <summary>
/// What a rescan did, in a sentence too, and what it found.
/// </summary>
/// <param name="Mods">How many mods it rescanned.</param>
/// <param name="Added">The files found on disk and matched, now in the library.</param>
/// <param name="Hashed">The library's files whose SHA-256 it read.</param>
/// <param name="Missing">The library's files that aren't on disk.</param>
/// <param name="Unmatched">The files in the mods' folders that match none of their mods' files, by absolute path.</param>
/// <param name="Failures">The mods whose files couldn't be matched, because their sites couldn't be asked.</param>
internal sealed record RescanResult(
    string Message,
    int Mods,
    IReadOnlyList<RescannedFile> Added,
    IReadOnlyList<RescannedFile> Hashed,
    IReadOnlyList<RescannedFile> Missing,
    IReadOnlyList<string> Unmatched,
    IReadOnlyList<RescanFailureEntry> Failures);
