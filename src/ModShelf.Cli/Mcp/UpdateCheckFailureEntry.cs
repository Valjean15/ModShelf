namespace ModShelf.Cli.Mcp;

/// <summary>
/// A game, or one of its mods, that a check couldn't check.
/// </summary>
internal sealed record UpdateCheckFailureEntry(string Source, string Game, string? ModId, string Reason);
