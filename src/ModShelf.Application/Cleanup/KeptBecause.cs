namespace ModShelf.Application.Cleanup;

/// <summary>
/// Why a file that's in a <see cref="CleanupGroup"/> is kept rather than offered to go: it may be the only copy left, or
/// it may be the mod's after all (ADR 0104).
/// </summary>
public enum KeptBecause
{
    /// <summary>
    /// The mod is gone from its site, hidden or removed there, so the library's copy may be the only one left.
    /// </summary>
    ModGone = 0,

    /// <summary>
    /// The mod's site no longer offers the file: it isn't listed, or is listed as archived.
    /// </summary>
    NotOffered = 1,

    /// <summary>
    /// The mod's site couldn't be asked which files it offers. <see cref="CleanupEntry.KeptDetail"/> says why.
    /// </summary>
    NotChecked = 2,

    /// <summary>
    /// The file matches one of the mod's files on its site by its name and size, though the library doesn't have it: a
    /// rescan adds it. <see cref="CleanupEntry.KeptDetail"/> is the site's name of that file.
    /// </summary>
    MatchesSiteFile = 3,
}
