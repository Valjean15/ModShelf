using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Opens a game's own settings on the Settings screen, as the Library's gear on a game does.
/// </summary>
public interface IGameSettingsOpener
{
    /// <summary>
    /// Raised with the game whose settings are to open, before the Settings screen shows.
    /// </summary>
    event EventHandler<GameRef>? Opening;

    /// <summary>
    /// Shows the Settings screen at <paramref name="game"/>'s own settings.
    /// </summary>
    void Open(GameRef game);
}
