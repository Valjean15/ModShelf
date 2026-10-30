namespace ModShelf.Presentation.Services;

/// <summary>
/// Ends the desktop app, as quitting from the tray does: its windows close, and the work running in the background stops.
/// </summary>
public interface IAppExit
{
    /// <summary>
    /// Quits the app. It ends once the windows are closed and the background work has stopped, so this returns first.
    /// </summary>
    void Quit();
}
