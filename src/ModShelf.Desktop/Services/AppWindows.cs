using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ModShelf.Desktop.Services;

/// <summary>
/// The app's windows, for the services that show something over one.
/// </summary>
internal static class AppWindows
{
    /// <summary>
    /// The main window, when the app has one open.
    /// </summary>
    public static Window? Main
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } window
            ? window
            : null;

    /// <summary>
    /// The window a dialog goes over: the newest one open, such as the first-run setup over the main window; or the main
    /// window, or none.
    /// </summary>
    public static Window? Top
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.LastOrDefault(window => window.IsVisible)
            ?? Main;
}
