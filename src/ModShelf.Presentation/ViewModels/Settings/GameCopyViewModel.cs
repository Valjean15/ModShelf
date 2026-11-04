using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// On a game's settings, the same game on another site, whose settings it can take: the user chooses it, after a
/// question.
/// </summary>
public sealed partial class GameCopyViewModel
{
    private readonly Func<GameRef, Task> _copy;

    /// <param name="game">The game on the other site.</param>
    /// <param name="text">Such as "Copy settings from Stardew Valley (Example Mods)".</param>
    /// <param name="copy">Asks, then copies <paramref name="game"/>'s settings to the page's game.</param>
    internal GameCopyViewModel(GameRef game, string text, Func<GameRef, Task> copy)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(copy);

        Game = game;
        Text = text;
        _copy = copy;
    }

    /// <summary>
    /// The game on the other site.
    /// </summary>
    public GameRef Game { get; }

    /// <summary>
    /// Such as "Copy settings from Stardew Valley (Example Mods)".
    /// </summary>
    public string Text { get; }

    [RelayCommand]
    private Task CopyAsync() => _copy(Game);
}
