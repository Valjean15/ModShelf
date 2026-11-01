namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file of a mod, as its site lists it.
/// </summary>
/// <param name="Link">The file's page on its site, which <c>download</c> takes.</param>
/// <param name="ReplacedBy">The newer file that replaces it, by the site's word.</param>
/// <param name="Linked">Whether the link asked about points to this file.</param>
/// <param name="LibraryPath">Where the library has it, an absolute path; null when it doesn't.</param>
internal sealed record ModFileEntry(
    string FileId,
    string Name,
    string? Version,
    string Category,
    string FileName,
    long? SizeBytes,
    DateTimeOffset UploadedAt,
    string? Link,
    string? ReplacedBy,
    bool Linked,
    string? LibraryPath);
