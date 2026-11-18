namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A file found by its MD5, from <c>v1/games/{game}/mods/md5_search/{md5}.json</c>, with its mod.
/// </summary>
internal sealed record NexusHashMatch(NexusMod? Mod, NexusFile? FileDetails);
