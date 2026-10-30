using Avalonia.Input.Platform;
using Avalonia.Threading;


namespace ModShelf.Desktop.Services;

/// <summary>
/// The clipboard of the main window's system, through Avalonia.
/// </summary>
internal sealed class AvaloniaClipboard : Presentation.Services.IClipboard
{
    /// <exception cref="InvalidOperationException">The main window isn't open, so there's no clipboard to reach.</exception>
    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Avalonia.Input.Platform.IClipboard clipboard = AppWindows.Main?.Clipboard
                ?? throw new InvalidOperationException("The main window isn't open, so the clipboard can't be reached.");
            await clipboard.SetTextAsync(text);
        });
    }
}
