namespace ModShelf.Application.Desktop.Ports;

/// <summary>
/// Whether the system shows the desktop app's icon in its tray: the notification area on Windows, the menu bar on
/// macOS, and on Linux a tray host, which most desktops run but stock GNOME doesn't. Platform implements it.
/// </summary>
public interface ITrayHost
{
    /// <summary>
    /// Whether an icon put in the tray now would show, so the app can be found there with its window closed.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
