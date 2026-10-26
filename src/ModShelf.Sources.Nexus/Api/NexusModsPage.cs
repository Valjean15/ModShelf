namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the <c>SearchMods</c> query answers: <c>data.mods</c>.
/// </summary>
internal sealed record NexusModsPage(NexusModsPage.Result? Mods)
{
    /// <param name="TotalCount">How many mods match in all.</param>
    internal sealed record Result(int TotalCount, IReadOnlyList<NexusModNode>? Nodes);
}
