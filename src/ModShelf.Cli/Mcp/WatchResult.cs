namespace ModShelf.Cli.Mcp;

/// <summary>
/// Whether a mod is watched now, in a sentence too, and the mod as the library has it; null when it isn't in it.
/// </summary>
internal sealed record WatchResult(bool Watched, string Message, LibraryModEntry? Mod);
