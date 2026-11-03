namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod's note now, in a sentence too, and the mod as the library has it.
/// </summary>
/// <param name="Note">The note kept; null when the mod has none.</param>
internal sealed record NoteResult(string? Note, string Message, LibraryModEntry Mod);
