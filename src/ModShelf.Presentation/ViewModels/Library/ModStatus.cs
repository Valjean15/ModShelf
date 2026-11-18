namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One thing a library mod's row says of it, such as "Update to 1.4" or "1 file missing".
/// </summary>
/// <param name="Detail">More of it, for a tooltip, such as since when; null for nothing more.</param>
public sealed record ModStatus(string Text, ModStatusKind Kind, string? Detail = null);
