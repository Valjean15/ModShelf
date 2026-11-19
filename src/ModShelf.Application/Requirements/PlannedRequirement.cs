using ModShelf.Domain.Mods;

namespace ModShelf.Application.Requirements;

/// <summary>
/// Something a mod needs, as its author listed it on the site, and what downloading the mod with its requirements does
/// with it (ADR 0102).
/// </summary>
/// <param name="Name">What's needed, as the author named it.</param>
/// <param name="Mod">The mod needed, when it's one of the site's; null for a thing off the site.</param>
/// <param name="PageUrl">Where a thing off the site is found.</param>
/// <param name="Notes">What the author said of it, such as which files need it.</param>
/// <param name="Depth">1 for what the mod itself needs; 2 and 3 for what those need in turn.</param>
/// <param name="NeededBy">The mod that lists it.</param>
/// <param name="File">The file downloading it takes, its newest main file, when it's to be downloaded.</param>
/// <param name="Why">Why it can't be downloaded.</param>
public sealed record PlannedRequirement(
    string Name,
    ModRef? Mod,
    Uri? PageUrl,
    string? Notes,
    int Depth,
    ModRef NeededBy,
    RequirementState State,
    ModFile? File = null,
    string? Why = null);
