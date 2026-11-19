namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file in the library's folders that could go, to free up space, or that's kept, and why.
/// </summary>
/// <param name="Group">Why it could go: archived, duplicates, unwatched or unmatched.</param>
/// <param name="ModId">The mod it's a file of; for an unmatched file, the mod whose folder it's in.</param>
/// <param name="FileId">The file's ID on its site; null for an unmatched file, which the library doesn't have.</param>
/// <param name="Path">Where it is, an absolute path.</param>
/// <param name="SameAs">For a duplicate, the absolute path of the copy kept.</param>
/// <param name="CanGo">Whether it can go: it isn't kept.</param>
/// <param name="Kept">
/// Why it's kept: modGone (its mod left its site), notOffered (the site no longer offers it), notChecked (the site
/// couldn't be asked) or matchesSiteFile (it's one of the mod's files on its site, which library_rescan adds); null when
/// it can go.
/// </param>
/// <param name="KeptDetail">For notChecked, why; for matchesSiteFile, the site's name of the file.</param>
internal sealed record CleanupFileEntry(
    string Group,
    string Source,
    string Game,
    string ModId,
    string ModName,
    string? FileId,
    string? Version,
    string Path,
    long SizeBytes,
    string? SameAs,
    bool CanGo,
    string? Kept,
    string? KeptDetail);
