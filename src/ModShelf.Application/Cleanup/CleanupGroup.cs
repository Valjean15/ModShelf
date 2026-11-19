namespace ModShelf.Application.Cleanup;

/// <summary>
/// Why a file in the library's folders could go, to free up space (ADR 0104). A file is in the first group that fits,
/// in the order <see cref="Duplicates"/>, <see cref="Archived"/>, <see cref="Unwatched"/>; <see cref="Unmatched"/> files
/// aren't the library's, so they're in no other.
/// </summary>
/// <remarks>The numeric values are the order the groups are shown in.</remarks>
public enum CleanupGroup
{
    /// <summary>
    /// An old version the library moved into its folder's <c>_archive</c> folder.
    /// </summary>
    Archived = 0,

    /// <summary>
    /// A library file with the same SHA-256 and size as another, which is kept.
    /// </summary>
    Duplicates = 1,

    /// <summary>
    /// A file of a mod that's no longer watched.
    /// </summary>
    Unwatched = 2,

    /// <summary>
    /// A file in a mod's folder that the library doesn't have and that matches none of the mod's files on its site, as
    /// a rescan reports it.
    /// </summary>
    Unmatched = 3,
}
