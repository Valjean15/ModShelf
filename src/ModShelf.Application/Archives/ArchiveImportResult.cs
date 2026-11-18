namespace ModShelf.Application.Archives;

/// <summary>
/// What importing archives did.
/// </summary>
/// <param name="Imported">The archives copied into the library, in order.</param>
/// <param name="ModsAdded">How many of their mods were new to the library, and are watched now.</param>
/// <param name="InLibrary">How many the library had by the time they came up, such as from an import stopped before.</param>
/// <param name="Problems">The archives that couldn't be imported, and why.</param>
public sealed record ArchiveImportResult(
    IReadOnlyList<ImportedArchive> Imported,
    int ModsAdded,
    int InLibrary,
    IReadOnlyList<ArchiveProblem> Problems);
