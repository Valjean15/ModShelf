namespace ModShelf.Application.Requirements;

/// <summary>
/// What downloading a mod with its requirements does with one of them (ADR 0102).
/// </summary>
public enum RequirementState
{
    /// <summary>
    /// A mod of the site the library lacks: its file, the newest main one, is downloaded, unless the user leaves it out.
    /// </summary>
    ToDownload = 1,

    /// <summary>
    /// The library has files of the mod already.
    /// </summary>
    InLibrary = 2,

    /// <summary>
    /// A download of one of the mod's files is under way.
    /// </summary>
    Downloading = 3,

    /// <summary>
    /// A thing off the site, such as a script extender: only its page is offered, never downloaded.
    /// </summary>
    Elsewhere = 4,

    /// <summary>
    /// A mod of the site that can't be downloaded, such as one without a main file: <see cref="PlannedRequirement.Why"/>
    /// says why.
    /// </summary>
    CantDownload = 5,
}
