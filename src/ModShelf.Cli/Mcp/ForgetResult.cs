namespace ModShelf.Cli.Mcp;

/// <summary>
/// Whether a mod left the library, in a sentence too, and the mod as the library had it, with the paths where its files
/// stay; null when it wasn't in the library.
/// </summary>
internal sealed record ForgetResult(bool Forgotten, string Message, LibraryModEntry? Mod);
