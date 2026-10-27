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
}
