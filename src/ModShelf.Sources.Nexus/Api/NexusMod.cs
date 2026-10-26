namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod's details, from <c>v1/games/{game}/mods/{mod}.json</c>. Hidden and removed mods come back with less.
/// </summary>
/// <param name="Status">Such as <c>published</c>, <c>hidden</c> or <c>under_moderation</c>.</param>
/// <param name="UpdatedTimestamp">When the mod last changed, in seconds since 1970.</param>
/// <param name="ModDownloads">How many times the mod's files were downloaded.</param>
internal sealed record NexusMod(
    long ModId,
    string? Name,
    string? Summary,
    string? Author,
    string? Version,
    string? PictureUrl,
    long? UpdatedTimestamp,
    bool? Available,
    string? Status,
    bool? ContainsAdultContent,
    long? ModDownloads = null,
    long? EndorsementCount = null);
