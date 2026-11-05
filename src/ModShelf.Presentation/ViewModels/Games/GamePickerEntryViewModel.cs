using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Games;

/// <summary>
/// A game in the game picker's list, or chosen in its field: its cover, name and site, and what the library has of it.
/// </summary>
public sealed partial class GamePickerEntryViewModel : ObservableObject
{
    private readonly Action<GamePickerEntryViewModel> _pick;

    /// <param name="siteName">The game's site's name, such as Nexus Mods.</param>
    /// <param name="pick">Chooses the game.</param>
    internal GamePickerEntryViewModel(GameRef game, string name, string siteName, ModPictures pictures, Action<GamePickerEntryViewModel> pick)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);
        ArgumentNullException.ThrowIfNull(pick);

        Game = game;
        Name = name;
        SiteName = siteName;
        _pick = pick;
        Cover = new GamePictureViewModel(pictures);
        CoverHue = Covers.HueOf(game.Key);
        Initial = Covers.InitialOf(name);
    }

    public GameRef Game { get; }

    public string Name { get; }

    /// <summary>
    /// The game's site's name, such as Nexus Mods, on its chip.
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Whether its site shows, as a chip: when the games listed are of two sites or more.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowSite { get; internal set; }

    /// <summary>
    /// Such as "21 mods in your library" or "Not in your library"; empty when there's nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string Meta { get; internal set; } = string.Empty;

    /// <summary>
    /// The heading of the group it starts, such as "Your games"; null for a game after the first of its group.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeading))]
    public partial string? Heading { get; internal set; }

    public bool HasHeading => Heading is not null;

    /// <summary>
    /// Whether Enter chooses it: the arrow keys move this from game to game.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; internal set; }

    /// <summary>
    /// Its cover: the game's banner from its site, once shown and read; its initial on its colour until then.
    /// </summary>
    public GamePictureViewModel Cover { get; }

    /// <summary>
    /// The hue of its cover, from 0 to 359, the same for the game every time.
    /// </summary>
    public int CoverHue { get; }

    public string Initial { get; }

    [RelayCommand]
    private void Pick() => _pick(this);
}
