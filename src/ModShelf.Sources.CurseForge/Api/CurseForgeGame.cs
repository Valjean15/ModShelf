namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A game on CurseForge, named in its links by its slug, such as <c>minecraft</c>.
/// </summary>
internal sealed record CurseForgeGame(long Id, string? Name, string? Slug, CurseForgeGameAssets? Assets);
