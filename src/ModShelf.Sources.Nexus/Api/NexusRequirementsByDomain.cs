namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the <c>ModRequirements</c> query answers: <c>data.legacyModsByDomain</c>, each mod asked for with what it needs
/// from Nexus Mods and elsewhere.
/// </summary>
internal sealed record NexusRequirementsByDomain(NexusRequirementsByDomain.Result? LegacyModsByDomain)
{
    internal sealed record Result(IReadOnlyList<ModNode>? Nodes);

    internal sealed record ModNode(long ModId, NexusModNode.GameNode? Game, Requirements? ModRequirements);

    internal sealed record Requirements(Page? NexusRequirements);

    internal sealed record Page(int TotalCount, IReadOnlyList<NexusRequirement?>? Nodes);
}
