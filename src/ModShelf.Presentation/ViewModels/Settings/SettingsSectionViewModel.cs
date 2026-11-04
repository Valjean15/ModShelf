using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Common;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A part of the Settings screen, such as the theme: the category it's in, and whether it shows, as its category's or as
/// a search's result.
/// </summary>
public sealed partial class SettingsSectionViewModel : ObservableObject
{
    private readonly string _words;

    /// <param name="words">What the section says, which a search looks in: its heading, what it explains, its choices.</param>
    internal SettingsSectionViewModel(SettingsCategory category, params string[] words)
    {
        ArgumentNullException.ThrowIfNull(words);

        Category = category;
        _words = string.Join(' ', words);
    }

    public SettingsCategory Category { get; }

    [ObservableProperty]
    public partial bool IsShown { get; internal set; }

    /// <summary>
    /// Whether each word searched for starts a word the section says, as <see cref="NameQuery"/> reads names: in any
    /// case, with or without accents.
    /// </summary>
    internal bool Matches(NameQuery search) => search.Matches(_words);
}
