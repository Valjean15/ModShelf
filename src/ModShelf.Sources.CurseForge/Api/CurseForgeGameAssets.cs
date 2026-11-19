namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A game's pictures.
/// </summary>
internal sealed record CurseForgeGameAssets(string? IconUrl, string? TileUrl, string? CoverUrl);
