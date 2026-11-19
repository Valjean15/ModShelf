using ModShelf.Application.Requirements;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Requirements as the test says: the plan for each mod, one level and deeper, or a failure. Queueing isn't used by the
/// desktop app, which queues through the library's downloads.
/// </summary>
internal sealed class ScriptedRequirementDownloads : IRequirementDownloads
{
    /// <summary>
    /// What each mod needs, one level down; those deeper are in <see cref="Deeper"/>.
    /// </summary>
    public Dictionary<ModRef, List<PlannedRequirement>> Plans { get; } = [];

    /// <summary>
    /// What's added when asked to go deeper, by mod.
    /// </summary>
    public Dictionary<ModRef, List<PlannedRequirement>> Deeper { get; } = [];

    /// <summary>
    /// What planning throws instead, while set.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Whether the site lists requirements at all.
    /// </summary>
    public bool Listed { get; set; } = true;

    public List<(ModRef Mod, bool Deeper)> Planned { get; } = [];

    public Task<RequirementsPlan> PlanAsync(ModRef modRef, bool deeper, CancellationToken cancellationToken)
    {
        Planned.Add((modRef, deeper));
        if (Failure is { } failure)
        {
            return Task.FromException<RequirementsPlan>(failure);
        }

        List<PlannedRequirement> planned = [.. Plans.GetValueOrDefault(modRef) ?? []];
        if (deeper)
        {
            planned.AddRange(Deeper.GetValueOrDefault(modRef) ?? []);
        }

        return Task.FromResult(new RequirementsPlan(modRef, Listed ? planned : [], Listed, deeper));
    }

    public Task<RequirementsPlan> PlanAsync(Uri link, bool deeper, CancellationToken cancellationToken)
        => throw new NotSupportedException("The desktop app plans from the mod's page.");

    public Task<RequirementDownloads> QueueAsync(IReadOnlyList<PlannedRequirement> chosen, CancellationToken cancellationToken)
        => throw new NotSupportedException("The desktop app queues through the library's downloads.");
}
