namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod in the library: watched for updates, or with downloaded files, or both.
/// </summary>
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
    IReadOnlyList<LibraryFileEntry> Files,
    IReadOnlyList<LibraryUpdateEntry> Updates);
