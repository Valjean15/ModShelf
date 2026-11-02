namespace ModShelf.Cli.Mcp;

/// <summary>
/// A file of a mod, as its site lists it.
/// </summary>
/// <param name="Link">The file's page on its site, which <c>download</c> takes.</param>
/// <param name="ReplacedBy">The newer file that replaces it, by the site's word.</param>
/// <param name="ReplacedByOnSite">
/// Whether the site still lists that newer file: false when it's hidden or removed, which the library may still have
/// (the mod's <c>libraryOnly</c>); null when nothing replaces this one.
/// </param>
/// <param name="Linked">Whether the link asked about points to this file.</param>
/// <param name="LibraryPath">Where the library has it, an absolute path; null when it doesn't.</param>
/// <param name="LibrarySha256">
/// The SHA-256 of the library's copy, read when it was recorded; null when the library doesn't have it, or has it without
/// a hash, as one recorded before ModShelf kept hashes.
/// </param>
/// <param name="Missing">Whether the library has it, but it's gone from disk.</param>
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
    bool? ReplacedByOnSite,
    bool Linked,
    string? LibraryPath,
    string? LibrarySha256,
    bool Missing);
