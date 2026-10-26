namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A GraphQL answer from <c>v2/graphql</c>: the data asked for, or the errors that kept the API from giving it. GraphQL
/// reports most errors with HTTP 200, so the errors are checked even when the request succeeded.
/// </summary>
internal sealed record NexusGraphQlAnswer<T>(T? Data, IReadOnlyList<NexusGraphQlError>? Errors);
