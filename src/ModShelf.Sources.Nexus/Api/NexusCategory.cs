namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One of a game's mod categories, from <c>v1/games/{game}.json</c>. Its parent, given as a number or <c>false</c>, isn't
/// read.
/// </summary>
internal sealed record NexusCategory(long CategoryId, string? Name);
