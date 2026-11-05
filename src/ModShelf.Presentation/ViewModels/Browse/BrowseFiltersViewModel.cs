using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Browse's filter card, over the results: the order, how recently the mods were updated, the category, the author,
/// adult content and the page size, as far as the game's site can search by them. What's changed here applies with
/// <see cref="ApplyCommand"/>; <see cref="CloseCommand"/> leaves what was applied, and <see cref="ResetCommand"/> goes
/// back to how the game starts.
/// </summary>
public sealed partial class BrowseFiltersViewModel : ObservableObject
{
    private readonly BrowseViewModel _screen;

    internal BrowseFiltersViewModel(BrowseViewModel screen)
    {
        _screen = screen;
    }

    /// <summary>
    /// Whether the card shows.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    /// <inheritdoc cref="BrowseViewModel.Sorts"/>
    [ObservableProperty]
    public partial IReadOnlyList<SortChoice> Sorts { get; private set; } = [];

    [ObservableProperty]
    public partial SortChoice? SelectedSort { get; set; }

    /// <inheritdoc cref="BrowseViewModel.TimeWindows"/>
    [ObservableProperty]
    public partial IReadOnlyList<TimeWindowChoice> TimeWindows { get; private set; } = [];

    [ObservableProperty]
    public partial TimeWindowChoice? SelectedTimeWindow { get; set; }

    /// <inheritdoc cref="BrowseViewModel.Categories"/>
    public ObservableCollection<CategoryChoice> Categories => _screen.Categories;

    [ObservableProperty]
    public partial CategoryChoice? SelectedCategory { get; set; }

    /// <summary>
    /// Only mods by this author; empty for any.
    /// </summary>
    [ObservableProperty]
    public partial string Author { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeAdultContent { get; set; }

    /// <inheritdoc cref="BrowseViewModel.PageSizes"/>
    [ObservableProperty]
    public partial IReadOnlyList<PageSizeChoice> PageSizes { get; private set; } = [];

    [ObservableProperty]
    public partial PageSizeChoice? SelectedPageSize { get; set; }

    /// <inheritdoc cref="BrowseViewModel.CanSearchByUpdated"/>
    [ObservableProperty]
    public partial bool CanSearchByUpdated { get; private set; }

    /// <inheritdoc cref="BrowseViewModel.CanSearchByCategory"/>
    [ObservableProperty]
    public partial bool CanSearchByCategory { get; private set; }

    /// <inheritdoc cref="BrowseViewModel.CanSearchByAuthor"/>
    [ObservableProperty]
    public partial bool CanSearchByAuthor { get; private set; }

    /// <inheritdoc cref="BrowseViewModel.CanIncludeAdult"/>
    [ObservableProperty]
    public partial bool CanIncludeAdult { get; private set; }

    /// <summary>
    /// The last reset, for tests to wait on.
    /// </summary>
    internal Task Resetting { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Opens the card on what's applied, offering what the game's site can search by.
    /// </summary>
    internal void Open()
    {
        Sorts = _screen.Sorts;
        TimeWindows = _screen.TimeWindows;
        PageSizes = _screen.PageSizes;
        CanSearchByUpdated = _screen.CanSearchByUpdated;
        CanSearchByCategory = _screen.CanSearchByCategory;
        CanSearchByAuthor = _screen.CanSearchByAuthor;
        CanIncludeAdult = _screen.CanIncludeAdult;
        Show(_screen.Applied());
        IsOpen = true;
    }

    /// <summary>
    /// Closes the card, leaving what was applied, as when another game is chosen.
    /// </summary>
    internal void Hide() => IsOpen = false;

    /// <summary>
    /// Closes the card, leaving what was applied.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => Hide();

    /// <summary>
    /// Applies what the card holds, closing it: the screen searches again with it, when it changed anything.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Apply()
    {
        BrowseFilterSet applied = _screen.Applied();
        BrowseFilterSet chosen = new(
            SelectedSort ?? applied.Sort,
            SelectedTimeWindow ?? applied.Window,
            SelectedCategory ?? Categories[0],
            Author.Trim(),
            IncludeAdultContent,
            SelectedPageSize ?? applied.PageSize);
        IsOpen = false;
        _screen.ApplyFilters(chosen);
    }

    /// <summary>
    /// Puts the card back to how the game starts, to apply: its own Browse settings, or Browse's.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private Task ResetAsync()
    {
        Resetting = ShowStartingAsync();
        return Resetting;
    }

    private async Task ShowStartingAsync() => Show(await _screen.StartingFiltersAsync());

    private void Show(BrowseFilterSet filters)
    {
        SelectedSort = filters.Sort;
        SelectedTimeWindow = filters.Window;
        SelectedCategory = filters.Category;
        Author = filters.Author;
        IncludeAdultContent = filters.IncludeAdultContent;
        SelectedPageSize = filters.PageSize;
    }
}
