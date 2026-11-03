namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file downloaded into the library lately, with its mod's other files there.
/// </summary>
/// <param name="Replaces">
/// The older version it replaced, and what became of it; null when it replaced none, or the library's history doesn't
/// say, as for a file found by a rescan.
/// </param>
internal sealed record NewLibraryFile(
    string Source,
    string Game,
    string ModId,
    string ModName,
    string? Page,
    LibraryFileEntry File,
    IReadOnlyList<LibraryFileEntry> OtherFiles,
    ReplacedFileEntry? Replaces);
