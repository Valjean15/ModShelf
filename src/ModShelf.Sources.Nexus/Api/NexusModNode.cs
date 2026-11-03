namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod in a GraphQL answer. Unlike v1, the names are camelCase and the dates ISO 8601.
/// </summary>
/// <param name="Status">Such as <c>published</c>.</param>
/// <param name="Category">The name of the mod's category; the search doesn't ask for it.</param>
/// <param name="ModCategory">The mod's category, with its name; the search doesn't ask for it.</param>
/// <param name="CreatedAt">When the mod was published; the mod's details don't ask for it.</param>
/// <param name="FileSize">Its main file's size, in KiB; the mod's details don't ask for it.</param>
internal sealed record NexusModNode(
    long ModId,
    string? Name,
    string? Summary,
    string? Author,
    string? Version,
    string? PictureUrl,
    string? ThumbnailUrl,
    string? ThumbnailBlurredUrl,
    DateTimeOffset? UpdatedAt,
    long? Endorsements,
    long? Downloads,
    bool? AdultContent,
    string? Status,
    NexusModNode.GameNode? Game,
    string? Category = null,
    NexusModNode.CategoryNode? ModCategory = null,
    DateTimeOffset? CreatedAt = null,
    long? FileSize = null)
{
    internal sealed record GameNode(string? DomainName);

    internal sealed record CategoryNode(long? CategoryId, string? Name);
}
