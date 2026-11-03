namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod in the library: watched for updates, or with downloaded files, or both.
/// </summary>
/// <param name="Pinned">
/// Whether the user pinned it at the version the library has: its updates are still found and listed here, but aren't
/// offered (<c>library_updates</c> leaves them out).
/// </param>
/// <param name="Note">The user's note of it; null for none.</param>
/// <param name="FileCount">How many files the library has of it.</param>
/// <param name="MissingCount">How many of those are gone from disk.</param>
/// <param name="UpdateCount">How many updates are offered for it: none for a pinned mod, and none skipped.</param>
/// <param name="Files">Its files, the newest first; null in a compact listing.</param>
/// <param name="Updates">Its updates, the newest first, skipped ones too; null in a compact listing.</param>
internal sealed record LibraryModEntry(
    string Source,
    string Game,
    string ModId,
    string Name,
    string? Author,
    string? Page,
    bool Watched,
    bool Pinned,
    string? Note,
    DateTimeOffset AddedAt,
    DateTimeOffset? CheckedAt,
    int FileCount,
    int MissingCount,
    int UpdateCount,
    IReadOnlyList<LibraryFileEntry>? Files,
    IReadOnlyList<LibraryUpdateEntry>? Updates);
