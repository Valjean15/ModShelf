namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod in <c>v1/games/{game}/mods/updated.json</c>, with its times as Unix seconds.
/// </summary>
/// <param name="LatestFileUpdate">When a file was last added or changed. It can be long before the period asked for.</param>
/// <param name="LatestModActivity">When anything about the mod last changed, its page and comments included.</param>
internal sealed record NexusUpdatedMod(long ModId, long? LatestFileUpdate, long? LatestModActivity);
