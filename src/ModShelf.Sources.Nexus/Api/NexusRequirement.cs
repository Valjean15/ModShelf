namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One thing a mod needs, as the v2 API lists it: another mod on Nexus Mods, named by its game's number and its ID, or,
/// with <see cref="ExternalRequirement"/>, something elsewhere, at <see cref="Url"/>. The IDs are strings, such as
/// <c>"2400"</c>; empty notes and links are empty strings.
/// </summary>
/// <param name="GameId">The game's number, such as <c>"1303"</c>, not its domain name.</param>
internal sealed record NexusRequirement(
    string? Id,
    string? ModId,
    string? ModName,
    string? GameId,
    string? Notes,
    string? Url,
    bool? ExternalRequirement);
