namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod the account tracks, from <c>v1/user/tracked_mods.json</c>.
/// </summary>
/// <param name="DomainName">The game's domain name, such as <c>stardewvalley</c>.</param>
internal sealed record NexusTrackedMod(long ModId, string? DomainName);
