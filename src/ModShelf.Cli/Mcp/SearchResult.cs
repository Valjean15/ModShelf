namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of a search's mods: how many match, where the page starts, and whether there are more.
/// </summary>
internal sealed record SearchResult(int Total, int Offset, bool HasMore, IReadOnlyList<SearchEntry> Mods);
