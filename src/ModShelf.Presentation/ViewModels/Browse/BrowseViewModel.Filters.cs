using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The filters applied: set in the filter card, shown as chips under the search, and kept for each game while the app
/// runs.
/// </summary>
public sealed partial class BrowseViewModel
{
    // What each game had applied when another was chosen, to go back to when it's chosen again.
    private readonly Dictionary<GameRef, BrowseFilterSet> _keptFilters = [];

    /// <summary>
    /// The filter card.
    /// </summary>
    public BrowseFiltersViewModel Filters { get; }

    /// <summary>
    /// A chip per filter applied that narrows the search: how recently the mods were updated, the category, the author
    /// and adult content, as far as the game's site searches by them.
    /// </summary>
    public ObservableCollection<BrowseFilterChipViewModel> Chips { get; } = [];

    [ObservableProperty]
    public partial bool HasChips { get; private set; }

    /// <summary>
    /// The filter card's button: "Filters", or "Filters · 3" with three chips.
    /// </summary>
    [ObservableProperty]
    public partial string FiltersText { get; private set; } = Strings.BrowseFilters;

    /// <summary>
    /// The order of the search listed, such as "Order: Most endorsements"; empty while none is.
    /// </summary>
    [ObservableProperty]
    public partial string OrderText { get; private set; } = string.Empty;

    /// <summary>
    /// What's applied now.
    /// </summary>
    internal BrowseFilterSet Applied()
        => new(SelectedSort, SelectedTimeWindow, SelectedCategory ?? _allCategories, Author, IncludeAdultContent, SelectedPageSize);

    /// <summary>
    /// What the chosen game starts on: its own Browse settings, or Browse's own for what they leave out, every category, no
    /// author and no adult content.
    /// </summary>
    internal async Task<BrowseFilterSet> StartingFiltersAsync()
    {
        BrowseDefaults? defaults = null;
        if (Game is { } game)
        {
            try
            {
                defaults = (await _services.Settings.GetAsync(_stopping)).GameFor(game.Game).Browse;
            }
            catch (InvalidDataException)
            {
                // The Settings screen says what's wrong.
            }
        }

        return new BrowseFilterSet(StartingSort(defaults), _offer.SameTimeWindow(defaults?.UpdatedWithin), _allCategories, string.Empty, false, _offer.SamePageSize(defaults?.PageSize));
    }

    /// <summary>
    /// Applies <paramref name="filters"/>, once: searches again with them, as a change in the order does; or, when only
    /// adult content changed while the rows show, lists the rows again. Nothing changed searches nothing.
    /// </summary>
    internal void ApplyFilters(BrowseFilterSet filters)
    {
        BrowseFilterSet applied = Applied();
        if (filters == applied)
        {
            return;
        }

        _choosingQuietly = true;
        try
        {
            SelectedSort = filters.Sort;
            SelectedTimeWindow = filters.Window;
            SelectedCategory = filters.Category;
            Author = filters.Author;
            IncludeAdultContent = filters.IncludeAdultContent;
            SelectedPageSize = filters.PageSize;
        }
        finally
        {
            _choosingQuietly = false;
        }

        if (IsShowingRows && Game is { } game && filters with { IncludeAdultContent = applied.IncludeAdultContent } == applied)
        {
            RowsWork = ListRowsAsync(game.Game);
        }
        else
        {
            SearchAgain();
        }
    }

    partial void OnAuthorChanged(string value) => ShowChips();

    /// <summary>
    /// Opens the filter card, or closes it, leaving what was applied.
    /// </summary>
    [RelayCommand]
    private void ToggleFilters()
    {
        if (Filters.IsOpen)
        {
            Filters.Hide();
        }
        else
        {
            Filters.Open();
        }
    }

    /// <summary>
    /// Removes every filter the chips show, and searches again once.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
        => ApplyFilters(Applied() with { Window = TimeWindows[0], Category = _allCategories, Author = string.Empty, IncludeAdultContent = false });

    /// <summary>
    /// The game's own order, when the game's site has it, or the one Browse starts on.
    /// </summary>
    private SortChoice StartingSort(BrowseDefaults? defaults)
        => defaults?.Sort is { } sort ? _offer.SortOf(sort, defaults.Direction) ?? _offer.Default : _offer.Default;

    /// <summary>
    /// Keeps what the game had applied, for when it's chosen again.
    /// </summary>
    private void KeepFilters(GameRef game) => _keptFilters[game] = Applied();

    /// <summary>
    /// Applies what the game had when another was chosen, as far as its site still offers it, without searching. Its
    /// category is chosen as its categories are read.
    /// </summary>
    /// <returns>What it had; null for a game not chosen before while the app runs.</returns>
    private BrowseFilterSet? RestoreFilters(GameRef game)
    {
        if (!_keptFilters.TryGetValue(game, out BrowseFilterSet? kept))
        {
            return null;
        }

        _choosingQuietly = true;
        try
        {
            SelectedSort = _offer.SameSort(kept.Sort);
            SelectedTimeWindow = _offer.SameTimeWindow(kept.Window.Within);
            SelectedPageSize = _offer.SamePageSize(kept.PageSize.Count);
            Author = kept.Author;
            IncludeAdultContent = kept.IncludeAdultContent;
        }
        finally
        {
            _choosingQuietly = false;
        }

        return kept;
    }

    /// <summary>
    /// Shows a chip per filter applied that the game's site searches by, and counts them on the card's button.
    /// </summary>
    private void ShowChips()
    {
        List<BrowseFilterChipViewModel> chips = [];
        if (CanSearchByUpdated && SelectedTimeWindow?.Within is not null)
        {
            chips.Add(Chip(SelectedTimeWindow.Name, filters => filters with { Window = TimeWindows[0] }));
        }

        if (CanSearchByCategory && SelectedCategory?.Category is { } category)
        {
            chips.Add(Chip(Text.Format(Strings.BrowseCategoryChip, category), filters => filters with { Category = _allCategories }));
        }

        if (CanSearchByAuthor && !string.IsNullOrWhiteSpace(Author))
        {
            chips.Add(Chip(Text.Format(Strings.BrowseAuthorChip, Author.Trim()), filters => filters with { Author = string.Empty }));
        }

        if (CanIncludeAdult && IncludeAdultContent)
        {
            chips.Add(Chip(Strings.BrowseAdultChip, filters => filters with { IncludeAdultContent = false }));
        }

        if (!chips.Select(chip => chip.Label).SequenceEqual(Chips.Select(chip => chip.Label)))
        {
            Chips.Clear();
            foreach (BrowseFilterChipViewModel chip in chips)
            {
                Chips.Add(chip);
            }
        }

        HasChips = chips.Count > 0;
        FiltersText = chips.Count == 0 ? Strings.BrowseFilters : Text.Format(Strings.BrowseFiltersCount, chips.Count);
    }

    private BrowseFilterChipViewModel Chip(string label, Func<BrowseFilterSet, BrowseFilterSet> without)
        => new(label, Text.Format(Strings.BrowseChipRemove, label), () => ApplyFilters(without(Applied())));
}
