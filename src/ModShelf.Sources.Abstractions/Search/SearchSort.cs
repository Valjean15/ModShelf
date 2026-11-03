namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// The order of the mods a search finds. Each is described here most first; <see cref="SearchDirection.Ascending"/>
/// turns it around.
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

    /// <summary>
    /// By name, from Z to A, as the other orders put the most first; <see cref="SearchDirection.Ascending"/> gives A to Z.
    /// </summary>
    Name = 5,

    /// <summary>
    /// The largest first, by the size the site gives the mod, such as its main file's.
    /// </summary>
    Size = 6,

    /// <summary>
    /// The most downloaded first, counting each user once.
    /// </summary>
    UniqueDownloads = 7,
}
