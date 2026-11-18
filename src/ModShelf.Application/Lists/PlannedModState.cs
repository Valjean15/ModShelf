namespace ModShelf.Application.Lists;

/// <summary>
/// Where a listed mod stands before it's imported.
/// </summary>
public enum PlannedModState
{
    /// <summary>
    /// The library doesn't have it: importing adds it.
    /// </summary>
    New = 0,

    /// <summary>
    /// The library has it: importing adds only the files it lacks, and keeps its watching, pin and note as they are.
    /// </summary>
    InLibrary = 1,

    /// <summary>
    /// ModShelf can't reach its site here, such as a site this version doesn't know: it's left out.
    /// </summary>
    NoSite = 2,
}
