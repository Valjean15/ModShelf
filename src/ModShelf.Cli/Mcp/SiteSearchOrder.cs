namespace ModShelf.Cli.Mcp;

/// <summary>
/// An order a site's search sorts by, as <c>mod_search</c>'s <c>sort</c> takes it, and which ways round.
/// </summary>
/// <param name="Descending">Whether the most can come first, as without <c>ascending</c>.</param>
/// <param name="Ascending">Whether the least can come first, as with <c>ascending</c>.</param>
internal sealed record SiteSearchOrder(string Sort, bool Descending, bool Ascending);
