namespace ModShelf.Cli.Mcp;

/// <summary>
/// An update a check found: a newer file of a watched mod that the library doesn't have.
/// </summary>
/// <param name="Kind">
/// <c>update</c> for a newer version of a library file; <c>newFile</c> for a file that replaces none, such as a new
/// optional file of a mod with files, or a new main file of a mod that's only watched.
/// </param>
/// <param name="Link">The file's page on its site, which <c>download</c> takes.</param>
/// <param name="Replaces">The library's file this one replaces, by the site's word; null when it has none.</param>
/// <param name="Skipped">Whether the user skipped it until a newer update comes, so it isn't offered.</param>
internal sealed record LibraryUpdateEntry(
    string FileId,
    string Name,
    string? Version,
    string Category,
    string Kind,
    string FileName,
    DateTimeOffset UploadedAt,
    DateTimeOffset FoundAt,
    string? Link,
    LibraryFileEntry? Replaces,
    bool Skipped);
