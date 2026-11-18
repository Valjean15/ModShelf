namespace ModShelf.Application.Lists;

/// <summary>
/// Why part of a list couldn't be imported.
/// </summary>
public enum ListImportProblemKind
{
    /// <summary>
    /// ModShelf can't reach the mod's site here.
    /// </summary>
    NoSite = 0,

    /// <summary>
    /// The mod's site couldn't be asked about it, such as when it's offline or refused the account.
    /// </summary>
    SiteFailed = 1,

    /// <summary>
    /// The site no longer offers the file.
    /// </summary>
    FileGone = 2,

    /// <summary>
    /// The file couldn't be queued, or the mod watched.
    /// </summary>
    NotDone = 3,
}
