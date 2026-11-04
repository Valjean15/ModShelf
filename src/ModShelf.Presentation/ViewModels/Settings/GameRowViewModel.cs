using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A game in the Settings screen's Games category: its cover, its name, and whether it has settings of its own.
/// </summary>
public sealed partial class GameRowViewModel : ObservableObject
{
    internal GameRowViewModel(GameRef game, string name, ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(game);

        Game = game;
        CoverHue = Covers.HueOf(game.Key);
        Name = name;
        Cover = new GamePictureViewModel(pictures);
    }

    /// <summary>
    /// The game: its site, and its key there, such as skyrimspecialedition.
    /// </summary>
    public GameRef Game { get; }

    /// <summary>
    /// The hue of the game's cover, from its key.
    /// </summary>
    public int CoverHue { get; }

    /// <summary>
    /// The game's wide picture on its site, on its cover in place of its initial.
    /// </summary>
    public GamePictureViewModel Cover { get; }

    /// <summary>
    /// The game's name, once its site says it, with the site's when another site's game has the same name; its key until
    /// then.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    public partial string Name { get; internal set; }

    /// <summary>
    /// The first letter of the game's name, on its cover until the game has a picture of its own.
    /// </summary>
    public string Initial => Covers.InitialOf(Name);

    /// <summary>
    /// Such as "Uses the defaults", "2 settings overridden" or "Hidden".
    /// </summary>
    [ObservableProperty]
    public partial string Note { get; internal set; } = string.Empty;

    public override string ToString() => Name;
}
