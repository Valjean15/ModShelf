namespace ModShelf.Cli.Mcp;

/// <summary>
/// A library file a rescan added, hashed or found missing, with its mod.
/// </summary>
internal sealed record RescannedFile(string Source, string Game, string ModId, LibraryFileEntry File);
