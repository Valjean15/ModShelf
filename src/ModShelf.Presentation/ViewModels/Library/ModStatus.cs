namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One thing a library mod's row says of it, such as "Update to 1.4" or "1 file missing".
/// </summary>
public sealed record ModStatus(string Text, ModStatusKind Kind);
