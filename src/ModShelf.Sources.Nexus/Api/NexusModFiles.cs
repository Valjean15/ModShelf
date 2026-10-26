namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A mod's files, from <c>v1/games/{game}/mods/{mod}/files.json</c>.
/// </summary>
internal sealed record NexusModFiles(IReadOnlyList<NexusFile> Files);
