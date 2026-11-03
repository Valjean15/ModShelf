namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// One of the rows a game's Browse page shows before a search.
/// </summary>
public enum BrowseRowKind
{
    /// <summary>
    /// The mods the site says are trending, where it says.
    /// </summary>
    Trending,

    /// <summary>
    /// The newest mods.
    /// </summary>
    RecentlyAdded,

    /// <summary>
    /// The mods updated last.
    /// </summary>
    RecentlyUpdated,
}
