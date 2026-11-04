namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of a site's games whose names match: how many match, where the page starts, and whether there are more.
/// </summary>
internal sealed record GamesResult(int Total, int Offset, bool HasMore, IReadOnlyList<GameEntry> Games);
