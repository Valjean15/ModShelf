using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Records the games whose own settings a screen opens.
/// </summary>
internal sealed class RecordingGameSettingsOpener : IGameSettingsOpener
{
    public event EventHandler<GameRef>? Opening;

    public List<GameRef> Opened { get; } = [];

    public void Open(GameRef game)
    {
        Opened.Add(game);
        Opening?.Invoke(this, game);
    }
}
