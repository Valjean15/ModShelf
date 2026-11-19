namespace ModShelf.Cli.Mcp;

/// <summary>
/// What could go from the library's folders to free up space, in a sentence too, by group, and a page of its files.
/// </summary>
/// <param name="CanGoBytes">The size of every file that can go, on every page.</param>
/// <param name="Groups">The groups with files, in the order archived, duplicates, unwatched, unmatched.</param>
/// <param name="Total">How many files are listed, on every page.</param>
/// <param name="Offset">How many of them come before this page.</param>
/// <param name="HasMore">Whether more come after it: ask again with the next offset.</param>
internal sealed record CleanupListing(
    string Message,
    long CanGoBytes,
    IReadOnlyList<CleanupGroupTotal> Groups,
    int Total,
    int Offset,
    bool HasMore,
    IReadOnlyList<CleanupFileEntry> Files);
