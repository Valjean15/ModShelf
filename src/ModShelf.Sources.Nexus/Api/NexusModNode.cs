namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod in a GraphQL answer. Unlike v1, the names are camelCase and the dates ISO 8601.
/// </summary>
/// <param name="Status">Such as <c>published</c>.</param>
internal sealed record NexusModNode(
    long ModId,
    string? Name,
    string? Summary,
    string? Author,
    string? Version,
    string? PictureUrl,
    DateTimeOffset? UpdatedAt,
    long? Endorsements,
    long? Downloads,
    bool? AdultContent,
    string? Status,
    NexusModNode.GameNode? Game)
{
    internal sealed record GameNode(string? DomainName);
}
