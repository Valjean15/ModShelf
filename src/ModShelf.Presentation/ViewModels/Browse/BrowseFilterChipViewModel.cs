using CommunityToolkit.Mvvm.Input;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A filter applied, under the search, such as "Category: Armour", whose × removes it and searches again.
/// </summary>
public sealed partial class BrowseFilterChipViewModel
{
    private readonly Action _remove;

    internal BrowseFilterChipViewModel(string label, string removeLabel, Action remove)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(remove);

        Label = label;
        RemoveLabel = removeLabel;
        _remove = remove;
    }

    /// <summary>
    /// Such as "Category: Armour" or "Updated in the last week".
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// What the × says to a screen reader, such as "Remove Category: Armour".
    /// </summary>
    public string RemoveLabel { get; }

    [RelayCommand]
    private void Remove() => _remove();
}
