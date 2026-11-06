using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Opens a screen at something on it: the screen told first, then shown.
/// </summary>
internal sealed class ScreenOpener(INavigationService navigation) : IScreenOpener
{
    private readonly INavigationService _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

    public event EventHandler<ModRef>? ModOpening;

    public event EventHandler<GameRef>? LibraryGameOpening;

    public event EventHandler<GameRef>? BrowseGameOpening;

    public event EventHandler<SettingsCategory>? SettingsOpening;

    public void OpenMod(ModRef libraryMod)
    {
        ArgumentNullException.ThrowIfNull(libraryMod);

        ModOpening?.Invoke(this, libraryMod);
        _navigation.NavigateTo(Screen.Library);
    }

    public void OpenInLibrary(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        LibraryGameOpening?.Invoke(this, game);
        _navigation.NavigateTo(Screen.Library);
    }

    public void OpenInBrowse(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        BrowseGameOpening?.Invoke(this, game);
        _navigation.NavigateTo(Screen.Browse);
    }

    public void OpenSettings(SettingsCategory category)
    {
        SettingsOpening?.Invoke(this, category);
        _navigation.NavigateTo(Screen.Settings);
    }
}
