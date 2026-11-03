using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A row of mods on a game's Browse page before a search, such as Trending: its title, its mods, and Show all, which
/// lists every mod in its order.
/// </summary>
public sealed partial class BrowseRowViewModel : ObservableObject
{
    private readonly Func<BrowseRowKind, Task>? _showAll;

    /// <param name="showAll">Lists every mod in the row's order; null for a row with no such order.</param>
    internal BrowseRowViewModel(BrowseRowKind kind, string title, Func<BrowseRowKind, Task>? showAll)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Kind = kind;
        Title = title;
        _showAll = showAll;
    }

    public BrowseRowKind Kind { get; }

    /// <summary>
    /// Such as "Trending".
    /// </summary>
    public string Title { get; }

    public ObservableCollection<BrowseModViewModel> Mods { get; } = [];

    /// <summary>
    /// Whether the row shows: not when the game's site doesn't list its mods that way.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; internal set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    /// <summary>
    /// Why the row couldn't be listed; null when it was, or is being listed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Whether the row was listed and has no mods.
    /// </summary>
    public bool IsEmpty => !IsLoading && Problem is null && Mods.Count == 0;

    public bool CanShowAll => _showAll is not null;

    /// <summary>
    /// Starts listing the row again, emptied. Runs on the UI thread.
    /// </summary>
    internal void Load()
    {
        Mods.Clear();
        Problem = null;
        IsLoading = true;
    }

    /// <summary>
    /// Shows the row's mods, listed. Runs on the UI thread.
    /// </summary>
    internal void Show(IEnumerable<BrowseModViewModel> mods)
    {
        foreach (BrowseModViewModel mod in mods)
        {
            Mods.Add(mod);
        }

        IsLoading = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Says why the row couldn't be listed. Runs on the UI thread.
    /// </summary>
    internal void Fail(string problem)
    {
        Problem = problem;
        IsLoading = false;
    }

    /// <summary>
    /// Stops showing it as being listed, as when a listing of another game replaced it. Runs on the UI thread.
    /// </summary>
    internal void Stop() => IsLoading = false;

    /// <summary>
    /// Lists every mod in the row's order, from the first page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowAll))]
    private Task ShowAllAsync() => _showAll?.Invoke(Kind) ?? Task.CompletedTask;
}
