namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One of the errors a GraphQL answer lists, such as a query the schema doesn't allow.
/// </summary>
internal sealed record NexusGraphQlError(string? Message);
