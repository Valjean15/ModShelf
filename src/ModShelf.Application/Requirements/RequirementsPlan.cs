using ModShelf.Domain.Mods;

namespace ModShelf.Application.Requirements;

/// <summary>
/// What a mod needs, as its site lists it, and what downloading it with its requirements would do (ADR 0102).
/// </summary>
/// <param name="Requirements">Each requirement once, in the site's order, level by level.</param>
/// <param name="Listed">Whether its site lists mods' requirements at all.</param>
/// <param name="Deeper">Whether the requirements of the mods it needs are listed too, down to the third level.</param>
public sealed record RequirementsPlan(ModRef Mod, IReadOnlyList<PlannedRequirement> Requirements, bool Listed, bool Deeper)
{
    /// <summary>
    /// The requirements to download, unless the user leaves some out.
    /// </summary>
    public IReadOnlyList<PlannedRequirement> ToDownload => [.. Requirements.Where(requirement => requirement.State == RequirementState.ToDownload)];
}
