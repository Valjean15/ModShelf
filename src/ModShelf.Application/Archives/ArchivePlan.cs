using ModShelf.Domain.Mods;

namespace ModShelf.Application.Archives;

/// <summary>
/// The archives of a folder, identified: what importing them would do (ADR 0101).
/// </summary>
/// <param name="Archives">Each archive, in the scan's order.</param>
public sealed record ArchivePlan(string Folder, GameRef Game, IReadOnlyList<PlannedArchive> Archives)
{
    /// <summary>
    /// The archives importing copies into the library.
    /// </summary>
    public IReadOnlyList<PlannedArchive> ToImport => [.. Archives.Where(archive => archive.State == ArchiveState.Identified)];

    /// <summary>
    /// The mods of <see cref="ToImport"/> new to the library, which importing watches.
    /// </summary>
    public int NewMods => ToImport.Where(archive => archive.Mod is not null).Select(archive => archive.File!.Mod).Distinct().Count();

    /// <summary>
    /// How many archives are in a state.
    /// </summary>
    public int Count(ArchiveState state) => Archives.Count(archive => archive.State == state);
}
