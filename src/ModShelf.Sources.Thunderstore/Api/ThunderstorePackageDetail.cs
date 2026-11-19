namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A package wherever it's listed, from <c>api/experimental/package/{team}/{name}/</c>: its names as Thunderstore
/// writes them, its newest version, and the communities that list it.
/// </summary>
/// <param name="Namespace">Its team's name.</param>
internal sealed record ThunderstorePackageDetail(
    string Namespace,
    string Name,
    ThunderstoreLatestVersion? Latest,
    IReadOnlyList<ThunderstoreCommunityListing>? CommunityListings);
