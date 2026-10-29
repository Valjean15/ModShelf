namespace ModShelf.Application.Settings;

/// <summary>
/// Whether the desktop app is light or dark.
/// </summary>
public enum AppTheme
{
    /// <summary>
    /// As the system's light or dark mode is, changing along with it.
    /// </summary>
    System,

    /// <summary>
    /// Light, whatever the system's mode.
    /// </summary>
    Light,

    /// <summary>
    /// Dark, whatever the system's mode.
    /// </summary>
    Dark,
}
