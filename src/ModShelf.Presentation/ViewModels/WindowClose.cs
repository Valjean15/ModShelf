namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// What closing the main window does.
/// </summary>
public enum WindowClose
{
    /// <summary>
    /// Nothing: the user changed their mind, so the window stays open.
    /// </summary>
    StayOpen,

    /// <summary>
    /// The window hides, and ModShelf keeps running in the tray.
    /// </summary>
    KeepRunningInTray,

    /// <summary>
    /// ModShelf ends.
    /// </summary>
    Quit,
}
