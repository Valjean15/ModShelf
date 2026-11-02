namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod in the library: watched for updates, or with downloaded files, or both.
/// </summary>
/// <param name="FileCount">How many files the library has of it.</param>
/// <param name="MissingCount">How many of those are gone from disk.</param>
/// <param name="UpdateCount">How many updates the checks found for it.</param>
/// <param name="Files">Its files, the newest first; null in a compact listing.</param>
/// <param name="Updates">Its updates, the newest first; null in a compact listing.</param>
internal sealed record LibraryModEntry(
    string Source,
    string Game,
    string ModId,
    string Name,
    string? Author,
    string? Page,
    bool Watched,
    DateTimeOffset AddedAt,
    DateTimeOffset? CheckedAt,
    int FileCount,
    int MissingCount,
    int UpdateCount,
    IReadOnlyList<LibraryFileEntry>? Files,
    IReadOnlyList<LibraryUpdateEntry>? Updates);
