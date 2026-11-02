namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the <c>ModDetails</c> query answers: <c>data.legacyModsByDomain</c>, the mods asked for by their game's domain
/// name and ID. A mod the API doesn't give, such as one that doesn't exist, isn't among the nodes.
/// </summary>
internal sealed record NexusModsByDomain(NexusModsByDomain.Result? LegacyModsByDomain)
{
    internal sealed record Result(int TotalCount, IReadOnlyList<NexusModNode>? Nodes);
}
