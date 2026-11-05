namespace ModShelf.Cli.Mcp;

/// <summary>
/// What the site's search can do for the game, so the next search asks only that: the arguments of <c>mod_search</c>
/// it takes, and their values.
/// </summary>
/// <param name="Site">The site's name, such as Nexus Mods.</param>
/// <param name="Orders">The orders it sorts by, the site's own first, and which ways round.</param>
/// <param name="Filters">The arguments that narrow a search it takes: words, author, category, updatedWithin and adult.</param>
/// <param name="MaxPageSize">The most mods its pages hold, the largest pageSize.</param>
/// <param name="Approvals">What the site calls its approvals, which endorsements counts, such as endorsements; null when it
/// has none.</param>
/// <param name="Categories">The game's categories, which category takes, in the site's order; null when the site doesn't
/// search by category, or they couldn't be read.</param>
internal sealed record SiteSearch(
    string Site,
    IReadOnlyList<SiteSearchOrder> Orders,
    IReadOnlyList<string> Filters,
    int MaxPageSize,
    string? Approvals,
    IReadOnlyList<string>? Categories);
