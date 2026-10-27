namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod's files, from <c>v1/games/{game}/mods/{mod}/files.json</c>.
/// </summary>
/// <param name="FileUpdates">Which file replaced which, oldest first; missing in some answers.</param>
internal sealed record NexusModFiles(IReadOnlyList<NexusFile> Files, IReadOnlyList<NexusFileUpdate>? FileUpdates = null);
