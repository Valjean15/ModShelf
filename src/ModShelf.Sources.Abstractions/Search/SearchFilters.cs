namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// What a site's search can narrow its results by, as <see cref="SearchQuery"/> asks.
/// </summary>
[Flags]
public enum SearchFilters
{
    /// <summary>
    /// Nothing: it lists a game's mods, in its orders.
    /// </summary>
    None = 0,

    /// <summary>
    /// Words in the mods' names (<see cref="SearchQuery.Text"/>).
    /// </summary>
    Text = 1,

    /// <summary>
    /// The mods' author (<see cref="SearchQuery.Author"/>).
    /// </summary>
    Author = 2,

    /// <summary>
    /// The mods' category (<see cref="SearchQuery.Category"/>).
    /// </summary>
    Category = 4,

    /// <summary>
    /// When the mods were last updated (<see cref="SearchQuery.UpdatedSince"/>).
    /// </summary>
    UpdatedSince = 8,

    /// <summary>
    /// Whether mods have adult content (<see cref="SearchQuery.IncludeAdultContent"/>). A site without it marks no mod as
    /// adult, so it has none to leave out or include.
    /// </summary>
    AdultContent = 16,
}
