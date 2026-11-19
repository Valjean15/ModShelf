namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A page of <c>api/cyberstorm/community/</c>: every listed community, in one page so far.
/// </summary>
/// <param name="Next">The next page's link, or null on the last.</param>
internal sealed record ThunderstoreCommunities(IReadOnlyList<ThunderstoreCommunity> Results, string? Next = null);
