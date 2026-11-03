using CommunityToolkit.Mvvm.ComponentModel;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One of the Settings screen's categories, as its list shows it: its name, and whether a search finds settings in it.
/// </summary>
public sealed partial class SettingsCategoryViewModel : ObservableObject
{
    internal SettingsCategoryViewModel(SettingsCategory category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Category = category;
        Name = name;
    }

    public SettingsCategory Category { get; }

    /// <summary>
    /// Such as "Library".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether it holds a setting the search finds, or no search is under way.
    /// </summary>
    [ObservableProperty]
    public partial bool HasMatches { get; internal set; } = true;

    public override string ToString() => Name;
}
