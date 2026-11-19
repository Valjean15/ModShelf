namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A package as a chunk of a community's listing gives it, in the format of <c>api/v1/package/</c>: what ModShelf reads of
/// it.
/// </summary>
/// <param name="Owner">Its team's name.</param>
/// <param name="RatingScore">How many users liked it.</param>
/// <param name="HasNsfwContent">Whether it has adult content.</param>
/// <param name="Versions">Its active versions, the newest first.</param>
internal sealed record ThunderstorePackage(
    string Name,
    string Owner,
    DateTimeOffset DateCreated,
    DateTimeOffset DateUpdated,
    long RatingScore,
    bool IsDeprecated,
    bool HasNsfwContent,
    IReadOnlyList<string>? Categories,
    IReadOnlyList<ThunderstoreVersion>? Versions);
