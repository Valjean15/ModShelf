using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Services;

/// <summary>
/// The games the settings hide from the lists (<see cref="GameSettings.Hidden"/>), as the screens last read them, so
/// each list leaves them out at once when the Settings screen hides one.
/// </summary>
public interface IHiddenGames
{
    /// <summary>
    /// Raised when the hidden games change.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// How many games are hidden.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Whether <paramref name="game"/>, by its site and its key in any case, is hidden. The same game on another site isn't
    /// hidden with it.
    /// </summary>
    bool IsHidden(GameRef game);

    /// <summary>
    /// Takes the hidden games from <paramref name="settings"/>, as read last, raising <see cref="Changed"/> when they
    /// differ.
    /// </summary>
    void Show(AppSettings settings);
}
