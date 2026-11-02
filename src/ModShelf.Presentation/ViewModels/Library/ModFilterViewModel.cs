using CommunityToolkit.Mvvm.ComponentModel;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One of the toggles that narrow a game's mods on the Library screen, with how many of the game's mods it would show.
/// </summary>
public sealed partial class ModFilterViewModel : ObservableObject
{
    private readonly Action _changed;

    internal ModFilterViewModel(ModFilterKind kind, string label, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        Kind = kind;
        Label = label;
        _changed = changed;
    }

    public ModFilterKind Kind { get; }

    /// <summary>
    /// Such as "Has an update".
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// How many of the game's mods it would show on its own.
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; internal set; }

    /// <summary>
    /// Whether only the mods it says show; with several on, only the mods all of them say.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => _changed();
}
