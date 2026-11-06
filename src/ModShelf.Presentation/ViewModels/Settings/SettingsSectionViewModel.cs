using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Common;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A part of the Settings screen, such as the theme: the category it's in, and whether it shows, as its category's or as
/// a search's result.
/// </summary>
public sealed partial class SettingsSectionViewModel : ObservableObject
{
    /// <param name="words">What the section says, which a search looks in: its heading first, what it explains, its choices.</param>
    internal SettingsSectionViewModel(SettingsCategory category, params string[] words)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Length == 0)
        {
            throw new ArgumentException("A section has a heading at least.", nameof(words));
        }

        Category = category;
        Heading = words[0];
        Words = string.Join(' ', words);
    }

    public SettingsCategory Category { get; }

    /// <summary>
    /// The section's heading, such as "Previews".
    /// </summary>
    public string Heading { get; }

    /// <summary>
    /// What the section says, which a search looks in.
    /// </summary>
    internal string Words { get; }

    [ObservableProperty]
    public partial bool IsShown { get; internal set; }

    /// <summary>
    /// Whether each word searched for starts a word the section says, as <see cref="NameQuery"/> reads names: in any
    /// case, with or without accents.
    /// </summary>
    internal bool Matches(NameQuery search) => search.Matches(Words);
}
