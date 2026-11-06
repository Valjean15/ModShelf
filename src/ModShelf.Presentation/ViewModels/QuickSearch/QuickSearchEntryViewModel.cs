using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.Common;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.QuickSearch;

/// <summary>
/// A row of quick search: what it is, its name, a line about it, what opening it does, and the heading of its group when
/// it's the group's first. Clicking it, or Enter while it's highlighted, opens it.
/// </summary>
public sealed partial class QuickSearchEntryViewModel : ObservableObject
{
    private readonly Action<QuickSearchEntryViewModel> _open;

    /// <param name="meta">Such as "In your library · 73 mods"; null for nothing.</param>
    /// <param name="action">Such as "Open in the Library", shown while it's highlighted; null for nothing.</param>
    /// <param name="picture">The game's cover; null for none, as for a mod, a setting or a screen.</param>
    /// <param name="colourKey">What the colour comes from, such as the game's key, as on its cover elsewhere; null for the
    /// name.</param>
    /// <param name="screen">The screen it opens, for its icon; null for one that isn't a screen.</param>
    internal QuickSearchEntryViewModel(
        QuickSearchKind kind,
        string name,
        string? meta,
        string? action,
        Action<QuickSearchEntryViewModel> open,
        GamePictureViewModel? picture = null,
        string? colourKey = null,
        Services.Screen? screen = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(open);

        Kind = kind;
        Name = name;
        Meta = meta;
        Action = action;
        _open = open;
        Picture = picture;
        Initial = Covers.InitialOf(name);
        Hue = Covers.HueOf(colourKey ?? name);
        Screen = screen;
    }

    public QuickSearchKind Kind { get; }

    public string Name { get; }

    public string? Meta { get; }

    public bool HasMeta => Meta is not null;

    public string? Action { get; }

    /// <summary>
    /// The game's cover, for a game; null otherwise.
    /// </summary>
    public GamePictureViewModel? Picture { get; }

    /// <summary>
    /// Whether the row shows a picture or a colour, as a game and a mod do, rather than an icon.
    /// </summary>
    public bool HasColour => Kind is QuickSearchKind.Game or QuickSearchKind.Mod;

    /// <summary>
    /// The initial on the colour, for a game without its picture yet, or a mod.
    /// </summary>
    public string Initial { get; }

    /// <summary>
    /// The colour's hue: a mod's is its game's.
    /// </summary>
    public int Hue { get; }

    /// <summary>
    /// The screen a screen's row opens, for its icon.
    /// </summary>
    public Services.Screen? Screen { get; }

    /// <summary>
    /// The heading of the row's group, such as "Games", on its first row; null on the others.
    /// </summary>
    [ObservableProperty]
    public partial string? Heading { get; internal set; }

    public bool HasHeading => Heading is not null;

    /// <summary>
    /// Whether Enter opens this row.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; internal set; }

    partial void OnHeadingChanged(string? value) => OnPropertyChanged(nameof(HasHeading));

    [RelayCommand]
    private void Open() => _open(this);
}
