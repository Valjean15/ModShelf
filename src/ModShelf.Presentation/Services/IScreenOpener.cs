using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Opens a screen at something on it, as quick search does: a mod's page or a game in the Library, a game in Browse, or a
/// category of the settings. The screen is told first, then shown.
/// </summary>
public interface IScreenOpener
{
    /// <summary>
    /// Raised with the library's mod whose page is to open in the Library.
    /// </summary>
    event EventHandler<ModRef>? ModOpening;

    /// <summary>
    /// Raised with the game whose mods are to show in the Library.
    /// </summary>
    event EventHandler<GameRef>? LibraryGameOpening;

    /// <summary>
    /// Raised with the game Browse is to search.
    /// </summary>
    event EventHandler<GameRef>? BrowseGameOpening;

    /// <summary>
    /// Raised with the category of the settings to show.
    /// </summary>
    event EventHandler<SettingsCategory>? SettingsOpening;

    /// <summary>
    /// Shows the Library at a mod's page.
    /// </summary>
    void OpenMod(ModRef libraryMod);

    /// <summary>
    /// Shows the Library at a game's mods.
    /// </summary>
    void OpenInLibrary(GameRef game);

    /// <summary>
    /// Shows Browse, on a game.
    /// </summary>
    void OpenInBrowse(GameRef game);

    /// <summary>
    /// Shows the Settings screen at a category.
    /// </summary>
    void OpenSettings(SettingsCategory category);
}
