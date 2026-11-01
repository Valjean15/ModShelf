namespace ModShelf.Cli.Mcp;

/// <summary>
/// An update a check found: a newer file of a watched mod that the library doesn't have.
/// </summary>
/// <param name="Link">The file's page on its site, which <c>download</c> takes.</param>
/// <param name="Replaces">The library's file this one replaces, by the site's word; null when it has none.</param>
internal sealed record LibraryUpdateEntry(
    string FileId,
    string Name,
    string? Version,
    string Category,
    string FileName,
    DateTimeOffset UploadedAt,
    DateTimeOffset FoundAt,
    string? Link,
    LibraryFileEntry? Replaces);
