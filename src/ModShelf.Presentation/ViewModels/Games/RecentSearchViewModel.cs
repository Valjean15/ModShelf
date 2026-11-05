using CommunityToolkit.Mvvm.Input;

namespace ModShelf.Presentation.ViewModels.Games;

/// <summary>
/// One of Browse's recent searches, in the game picker: choosing it searches it again in the chosen game.
/// </summary>
public sealed partial class RecentSearchViewModel
{
    private readonly Action<string> _search;

    internal RecentSearchViewModel(string text, Action<string> search)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(search);

        Text = text;
        _search = search;
    }

    /// <summary>
    /// What was searched for, such as "skyui".
    /// </summary>
    public string Text { get; }

    [RelayCommand]
    private void Search() => _search(Text);
}
