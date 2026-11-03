using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Opens a game's own settings: the Settings screen, told which game first, then shown.
/// </summary>
internal sealed class GameSettingsOpener(INavigationService navigation) : IGameSettingsOpener
{
    private readonly INavigationService _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

    public event EventHandler<GameRef>? Opening;

    public void Open(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        Opening?.Invoke(this, game);
        _navigation.NavigateTo(Screen.Settings);
    }
}
