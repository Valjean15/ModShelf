using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// What Browse's toolbar offers for a game, and a game's settings for how Browse starts: only what its site's search can
/// do, named in the site's words.
/// </summary>
/// <param name="Sorts">The orders, in Browse's order.</param>
/// <param name="Default">The order Browse starts on: the site's own, when Browse lists it.</param>
/// <param name="TimeWindows">How recently the mods were updated: any time, and more when the site can narrow by it.</param>
/// <param name="PageSizes">How many mods a page lists, as many as the site's pages hold at most.</param>
/// <param name="Filters">What the site can narrow a search by, for the boxes to show.</param>
internal sealed record BrowseOffer(
    IReadOnlyList<SortChoice> Sorts,
    SortChoice Default,
    IReadOnlyList<TimeWindowChoice> TimeWindows,
    IReadOnlyList<PageSizeChoice> PageSizes,
    SearchFilters Filters)
{
    /// <summary>
    /// The offered order <paramref name="sort"/> names, going as <paramref name="direction"/> says, or the order's own
    /// way; null when there's none such.
    /// </summary>
    public SortChoice? SortOf(SearchSort sort, SearchDirection? direction)
        => Sorts.FirstOrDefault(choice => choice.Sort == sort && (direction is null || choice.Direction == direction));

    /// <summary>
    /// The offered order that's the same as <paramref name="chosen"/>, or the one Browse starts on.
    /// </summary>
    public SortChoice SameSort(SortChoice? chosen)
        => (chosen is null ? null : SortOf(chosen.Sort, chosen.Direction)) ?? Default;

    /// <summary>
    /// The offered time that's the same as <paramref name="chosen"/>, or any time.
    /// </summary>
    public TimeWindowChoice SameTimeWindow(TimeSpan? chosen)
        => TimeWindows.FirstOrDefault(window => window.Within == chosen) ?? TimeWindows[0];

    /// <summary>
    /// The offered page size that's the same as <paramref name="chosen"/>, or the first.
    /// </summary>
    public PageSizeChoice SamePageSize(int? chosen)
        => PageSizes.FirstOrDefault(size => size.Count == chosen) ?? PageSizes[0];

    /// <summary>
    /// Whether the site can narrow a search by every one of <paramref name="filters"/>.
    /// </summary>
    public bool Has(SearchFilters filters) => (Filters & filters) == filters;
}
