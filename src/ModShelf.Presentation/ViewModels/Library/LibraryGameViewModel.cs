using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// A game's heading on the Library screen, above its mods; as cards, they're its own.
/// </summary>
public sealed partial class LibraryGameViewModel : ObservableObject
{
    internal LibraryGameViewModel(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        Game = game;
        Name = game.Key;
    }

    public GameRef Game { get; }

    /// <summary>
    /// The game's name on its site; its key, such as skyrimspecialedition, until the site says.
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; internal set; }

    /// <summary>
    /// Such as "12 mods": how many of its mods show.
    /// </summary>
    [ObservableProperty]
    public partial string ModsText { get; internal set; } = string.Empty;

    /// <summary>
    /// Its mods that show, by name, for the cards under its heading.
    /// </summary>
    public ObservableCollection<LibraryModViewModel> Mods { get; } = [];
}
