using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// What importing a list did.
/// </summary>
/// <param name="ModsAdded">The mods added to the library by watching them.</param>
/// <param name="Queued">The files queued for the library, in the order they were.</param>
/// <param name="FilesInLibrary">The listed files the library had already.</param>
/// <param name="Problems">What couldn't be done, in the list's order.</param>
public sealed record ListImportResult(
    int ModsAdded,
    IReadOnlyList<ModFileRef> Queued,
    int FilesInLibrary,
    IReadOnlyList<ListImportProblem> Problems)
{
    /// <summary>
    /// How many files were queued for the library.
    /// </summary>
    public int FilesQueued => Queued.Count;
}
