namespace ModShelf.Application.Archives;

/// <summary>
/// How an archive was told to be a site's file, the cheapest way first (ADR 0101).
/// </summary>
public enum ArchiveClue
{
    /// <summary>
    /// The note Mod Organizer 2 keeps beside it, naming the mod and the file, confirmed by the file's size on the site.
    /// </summary>
    Notes = 1,

    /// <summary>
    /// Its name, which names the mod as the site's archives do, confirmed by the name and size of one of its files there.
    /// </summary>
    Name = 2,

    /// <summary>
    /// A hash of its content, which the site found among the game's files.
    /// </summary>
    Hash = 3,
}
