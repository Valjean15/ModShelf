namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// What a community's packages can be filtered by, from <c>api/cyberstorm/community/{community}/filters/</c>: its
/// categories.
/// </summary>
internal sealed record ThunderstoreFilters(IReadOnlyList<ThunderstoreCategory> PackageCategories);
