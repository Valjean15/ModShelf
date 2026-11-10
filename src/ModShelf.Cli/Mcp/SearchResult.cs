namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of a search's mods: how many match, where the page starts, and whether there are more.
/// </summary>
/// <param name="LeftOut">
/// How many of the page's mods were left out for their adult content after the site answered, as a site that marks them
/// but can't leave them out needs; adult includes them. The page lists that many fewer, and the next one starts after them.
/// </param>
/// <param name="Search">What the site's search can do for the game, for the next search.</param>
internal sealed record SearchResult(int Total, int Offset, bool HasMore, int LeftOut, IReadOnlyList<SearchEntry> Mods, SiteSearch Search);
