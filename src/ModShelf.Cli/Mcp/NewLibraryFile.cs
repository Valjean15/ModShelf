namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file downloaded into the library lately, with its mod's other files there.
/// </summary>
internal sealed record NewLibraryFile(
    string Source,
    string Game,
    string ModId,
    string ModName,
    string? Page,
    LibraryFileEntry File,
    IReadOnlyList<LibraryFileEntry> OtherFiles);
