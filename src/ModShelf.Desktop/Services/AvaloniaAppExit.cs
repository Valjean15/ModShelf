using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Quits through Avalonia's lifetime, on the UI thread. The windows close for the app's shutdown, so the tray lets them,
/// and Avalonia takes the tray icon away as the app exits.
/// </summary>
internal sealed class AvaloniaAppExit : IAppExit
{
    public void Quit()
        => Dispatcher.UIThread.Post(() => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
}
