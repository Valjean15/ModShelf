namespace ModShelf.Presentation.Services;

/// <summary>
/// The system's clipboard, for text the user pastes elsewhere.
/// </summary>
public interface IClipboard
{
    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, in place of what was there.
    /// </summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}
