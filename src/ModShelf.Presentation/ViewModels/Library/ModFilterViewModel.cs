using CommunityToolkit.Mvvm.ComponentModel;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One of the toggles that narrow a game's mods on the Library screen, with how many of the game's mods it would show.
/// </summary>
public sealed partial class ModFilterViewModel : ObservableObject
{
    private readonly Action _changed;

    /// <param name="hidesWhenNone">Whether it shows only while it would show some mods, or is on.</param>
    internal ModFilterViewModel(ModFilterKind kind, string label, Action changed, bool hidesWhenNone = false)
    {
        ArgumentNullException.ThrowIfNull(changed);

        Kind = kind;
        Label = label;
        _changed = changed;
        HidesWhenNone = hidesWhenNone;
    }

    public ModFilterKind Kind { get; }

    /// <summary>
    /// Such as "Has an update".
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Whether it shows only while it would show some mods, or is on, as for a rare case such as mods gone from their
    /// sites.
    /// </summary>
    public bool HidesWhenNone { get; }

    /// <summary>
    /// How many of the game's mods it would show on its own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial int Count { get; internal set; }

    /// <summary>
    /// Whether only the mods it says show; with several on, only the mods all of them say.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool IsOn { get; set; }

    /// <summary>
    /// Whether it's offered: always, unless it <see cref="HidesWhenNone"/>.
    /// </summary>
    public bool IsShown => !HidesWhenNone || Count > 0 || IsOn;

    partial void OnIsOnChanged(bool value) => _changed();
}
