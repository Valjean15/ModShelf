using ModShelf.Application.Settings;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Makes the app light or dark. Desktop switches Avalonia's theme; ViewModel tests record it.
/// </summary>
public interface IThemeSwitcher
{
    /// <summary>
    /// Makes the app look as <paramref name="theme"/> says, at once, from whichever thread.
    /// </summary>
    void Apply(AppTheme theme);
}
