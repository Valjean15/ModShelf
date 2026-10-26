namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// The order of the mods a search finds.
/// </summary>
public enum SearchSort
{
    /// <summary>
    /// The best matches for the words first. Without words, the same as <see cref="Endorsements"/>.
    /// </summary>
    Relevance = 0,

    /// <summary>
    /// The most endorsed, liked or followed first, whatever the site counts as approval.
    /// </summary>
    Endorsements = 1,

    /// <summary>
    /// The most downloaded first.
    /// </summary>
    Downloads = 2,

    /// <summary>
    /// The most recently updated first.
    /// </summary>
    Updated = 3,

    /// <summary>
    /// The most recently published first.
    /// </summary>
    Newest = 4,
}
