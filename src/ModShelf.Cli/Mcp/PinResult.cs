namespace ModShelf.Cli.Mcp;

/// <summary>
/// Whether a mod is pinned now, in a sentence too, and the mod as the library has it.
/// </summary>
internal sealed record PinResult(bool Pinned, string Message, LibraryModEntry Mod);
