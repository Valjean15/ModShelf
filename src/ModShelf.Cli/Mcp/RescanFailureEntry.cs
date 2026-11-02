namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod whose files a rescan couldn't match, and why.
/// </summary>
internal sealed record RescanFailureEntry(string Source, string Game, string ModId, string Reason);
