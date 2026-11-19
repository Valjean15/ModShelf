namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A mod's picture, and a smaller one.
/// </summary>
internal sealed record CurseForgeLogo(string? ThumbnailUrl, string? Url);
