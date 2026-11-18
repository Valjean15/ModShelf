namespace ModShelf.Application.Lists;

/// <summary>
/// What importing a list would do, worked out from the library alone, before any site is asked.
/// </summary>
/// <param name="List">The list.</param>
/// <param name="Mods">Each of its mods, in its order.</param>
public sealed record ListImportPlan(ModList List, IReadOnlyList<PlannedMod> Mods)
{
    /// <summary>
    /// The mods the library doesn't have yet.
    /// </summary>
    public int NewMods => Mods.Count(mod => mod.State == PlannedModState.New);

    /// <summary>
    /// The mods the library has already.
    /// </summary>
    public int ModsInLibrary => Mods.Count(mod => mod.State == PlannedModState.InLibrary);

    /// <summary>
    /// The mods of sites ModShelf can't reach here.
    /// </summary>
    public int ModsWithoutSite => Mods.Count(mod => mod.State == PlannedModState.NoSite);

    /// <summary>
    /// The listed files marked optional, of mods whose site ModShelf can reach.
    /// </summary>
    public int OptionalFiles => Reachable.Sum(mod => mod.Files.Count(file => file.Listed.Optional && !file.InLibrary));

    /// <summary>
    /// The files importing would download, of mods whose site ModShelf can reach.
    /// </summary>
    public int FilesToDownload(bool includeOptional)
        => Reachable.Sum(mod => mod.Files.Count(file => !file.InLibrary && (includeOptional || !file.Listed.Optional)));

    /// <summary>
    /// The listed files the library has already.
    /// </summary>
    public int FilesInLibrary => Mods.Sum(mod => mod.Files.Count(file => file.InLibrary));

    private IEnumerable<PlannedMod> Reachable => Mods.Where(mod => mod.State != PlannedModState.NoSite);
}
