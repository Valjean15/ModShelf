using Avalonia.Styling;

using ModShelf.Application.Settings;
using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Makes the app light or dark through Avalonia's theme variant; the system's own follows its light or dark mode.
/// </summary>
internal sealed class AvaloniaThemeSwitcher(IUiThread uiThread) : IThemeSwitcher
{
    private readonly IUiThread _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));

    public void Apply(AppTheme theme)
    {
        ThemeVariant variant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        // At once on the UI thread, such as at start, before the window shows.
        _uiThread.Post(() =>
        {
            if (Avalonia.Application.Current is { } app)
            {
                app.RequestedThemeVariant = variant;
            }
        });
    }
}
