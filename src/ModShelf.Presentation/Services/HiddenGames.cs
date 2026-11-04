using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Services;

/// <summary>
/// The hidden games, as the screens last read them from the settings.
/// </summary>
internal sealed class HiddenGames : IHiddenGames
{
    private readonly Lock _gate = new();
    private HashSet<GameRef> _hidden = new(AppSettings.GameComparer);

    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _hidden.Count;
            }
        }
    }

    public bool IsHidden(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        lock (_gate)
        {
            return _hidden.Contains(game);
        }
    }

    public void Show(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HashSet<GameRef> hidden = new(settings.Games.Where(game => game.Value.Hidden == true).Select(game => game.Key), AppSettings.GameComparer);
        lock (_gate)
        {
            if (hidden.SetEquals(_hidden))
            {
                return;
            }

            _hidden = hidden;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
