using ModShelf.Domain.Mods;

namespace ModShelf.Application.Lists;

/// <summary>
/// Part of a list that couldn't be imported.
/// </summary>
/// <param name="Mod">The mod.</param>
/// <param name="ModName">Its name, as listed.</param>
/// <param name="File">The file, or null when the whole mod was left out.</param>
/// <param name="Kind">Why.</param>
/// <param name="Detail">What the site or ModShelf said, if anything.</param>
public sealed record ListImportProblem(ModRef Mod, string ModName, ModFileRef? File, ListImportProblemKind Kind, string? Detail);
