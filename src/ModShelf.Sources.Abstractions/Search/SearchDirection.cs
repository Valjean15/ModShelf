namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// Which end of the <see cref="SearchSort"/> comes first.
/// </summary>
public enum SearchDirection
{
    /// <summary>
    /// The most first: the best matches, the most endorsed or downloaded, the most recently updated or published.
    /// </summary>
    Descending = 0,

    /// <summary>
    /// The least first: the weakest matches, the least endorsed or downloaded, the longest since updated, the oldest.
    /// </summary>
    Ascending = 1,
}
