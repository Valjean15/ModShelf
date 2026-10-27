namespace ModShelf.Presentation.Services;

/// <summary>
/// Which screen the main window shows, and going to another, such as from a notice of new updates.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// The screen shown now; <see cref="Screen.Library"/> at first.
    /// </summary>
    Screen Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> changes, with the new screen, on the thread that changed it.
    /// </summary>
    event EventHandler<Screen>? Navigated;

    /// <summary>
    /// Shows <paramref name="screen"/>. Going to the screen shown already does nothing.
    /// </summary>
    void NavigateTo(Screen screen);
}
