namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// What a mod's card or row says besides what it always says, as fits the order it was listed in.
/// </summary>
internal enum BrowseFact
{
    None,

    /// <summary>
    /// Its size, such as "40.0 MB", in a search by size.
    /// </summary>
    Size,

    /// <summary>
    /// When it was published, such as "Added 3 days ago", among the newest.
    /// </summary>
    Published,

    /// <summary>
    /// When it was last updated, such as "Updated 2 hours ago", among the recently updated.
    /// </summary>
    Updated,
}
