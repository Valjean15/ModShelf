namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// Several mods asked for at once, by their IDs.
/// </summary>
internal sealed record CurseForgeModsRequest(IReadOnlyList<long> ModIds);
