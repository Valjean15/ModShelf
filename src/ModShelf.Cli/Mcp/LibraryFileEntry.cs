namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file the library has.
/// </summary>
/// <param name="Path">Where it is, an absolute path.</param>
/// <param name="Archived">Whether it's an old version moved to an <c>_archive</c> folder.</param>
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
    bool Archived);
