namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A category of a game's mods; one that's a class, such as Minecraft's Mods or Resource Packs, holds the others.
/// </summary>
internal sealed record CurseForgeCategory(long Id, long GameId, string? Name, string? Slug, bool? IsClass, long? ClassId);
