using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A game in the Settings screen's Games category: its cover's colour and initial, its name, and whether it has settings
/// of its own.
/// </summary>
public sealed partial class GameRowViewModel : ObservableObject
{
    internal GameRowViewModel(string game, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);

        Game = game;
        CoverHue = Covers.HueOf(game);
        Name = name;
    }

    /// <summary>
    /// The game as its site names it in links, such as skyrimspecialedition.
    /// </summary>
    public string Game { get; }

    /// <summary>
    /// The hue of the game's cover, from its key.
    /// </summary>
    public int CoverHue { get; }

    /// <summary>
    /// The game's name, once its site says it; its key until then.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    public partial string Name { get; internal set; }

    /// <summary>
    /// The first letter of the game's name, on its cover.
    /// </summary>
    public string Initial => Covers.InitialOf(Name);

    /// <summary>
    /// Such as "Uses the defaults", "2 settings overridden" or "Hidden".
    /// </summary>
    [ObservableProperty]
    public partial string Note { get; internal set; } = string.Empty;

    public override string ToString() => Name;
}
