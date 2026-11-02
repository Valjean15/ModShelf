namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file the library has.
/// </summary>
/// <param name="Path">Where it is, an absolute path.</param>
/// <param name="Archived">Whether it's an old version moved to an <c>_archive</c> folder.</param>
/// <param name="Missing">Whether it's gone from disk, deleted or moved outside ModShelf, though the library still has it.</param>
internal sealed record LibraryFileEntry(
    string FileId,
    string Name,
    string? Version,
    string Category,
    string FileName,
    string Path,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    DateTimeOffset DownloadedAt,
    bool Archived,
    bool Missing);
