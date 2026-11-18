namespace ModShelf.Application.Library;

/// <summary>
/// What happened in the library: a file came, moved or went, a mod left the library, or left its site or came back.
/// </summary>
public enum LibraryEventKind
{
    /// <summary>
    /// A file was downloaded into the library, perhaps replacing an older version.
    /// </summary>
    Downloaded = 0,

    /// <summary>
    /// An old version moved into its folder's <c>_archive</c> folder.
    /// </summary>
    Archived = 1,

    /// <summary>
    /// A file was deleted from the disk and the library: by the rule for old versions, or by the user.
    /// </summary>
    Deleted = 2,

    /// <summary>
    /// A rescan found a file placed in a mod's folder by hand, and added it.
    /// </summary>
    Found = 3,

    /// <summary>
    /// A mod was taken out of the library, its files left on disk.
    /// </summary>
    Removed = 4,

    /// <summary>
    /// A check found the mod gone from its site, hidden or removed there: the library's copies of its files may be the
    /// only ones left.
    /// </summary>
    LeftSite = 5,

    /// <summary>
    /// A check found a mod that had left its site back on it.
    /// </summary>
    BackOnSite = 6,
}
