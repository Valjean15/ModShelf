namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of a search's mods: how many match, where the page starts, and whether there are more.
/// </summary>
/// <param name="Search">What the site's search can do for the game, for the next search.</param>
internal sealed record SearchResult(int Total, int Offset, bool HasMore, IReadOnlyList<SearchEntry> Mods, SiteSearch Search);
