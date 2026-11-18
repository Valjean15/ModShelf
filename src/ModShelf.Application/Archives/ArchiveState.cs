namespace ModShelf.Application.Archives;

/// <summary>
/// What importing does with an archive.
/// </summary>
public enum ArchiveState
{
    /// <summary>
    /// It's a site's file the library lacks: importing copies it in.
    /// </summary>
    Identified = 1,

    /// <summary>
    /// The library has it already, by its content or as the site's file it is: it's left alone.
    /// </summary>
    InLibrary = 2,

    /// <summary>
    /// Another archive in the folder has the same content: it's left alone.
    /// </summary>
    Duplicate = 3,

    /// <summary>
    /// It couldn't be told to be one of the site's files: it's left alone, and <see cref="PlannedArchive.Why"/> says why.
    /// </summary>
    Unknown = 4,
}
