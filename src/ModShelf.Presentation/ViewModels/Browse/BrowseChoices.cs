using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The choices of Browse's toolbar that a game's own settings can start it on: the orders, how recently the mods were
/// updated, and the page sizes; each game offered only what its site's search can do (<see cref="For"/>).
/// </summary>
internal static class BrowseChoices
{
    /// <summary>
    /// The orders Browse lists, in its order; null names are approval's, in the site's words.
    /// </summary>
    private static readonly (SearchSort Sort, SearchDirection Direction, string? Name)[] Orders =
    [
        (SearchSort.Relevance, SearchDirection.Descending, Strings.BrowseSortBestMatch),
        (SearchSort.Endorsements, SearchDirection.Descending, null),
        (SearchSort.Endorsements, SearchDirection.Ascending, null),
        (SearchSort.Downloads, SearchDirection.Descending, Strings.BrowseSortMostDownloaded),
        (SearchSort.Downloads, SearchDirection.Ascending, Strings.BrowseSortLeastDownloaded),
        (SearchSort.Updated, SearchDirection.Descending, Strings.BrowseSortRecentlyUpdated),
        (SearchSort.Updated, SearchDirection.Ascending, Strings.BrowseSortLongestSinceUpdated),
        (SearchSort.Newest, SearchDirection.Descending, Strings.BrowseSortNewest),
        (SearchSort.Newest, SearchDirection.Ascending, Strings.BrowseSortOldest),
        (SearchSort.UniqueDownloads, SearchDirection.Descending, Strings.BrowseSortMostUniqueDownloads),
        (SearchSort.Size, SearchDirection.Descending, Strings.BrowseSortLargest),
        (SearchSort.Size, SearchDirection.Ascending, Strings.BrowseSortSmallest),
        (SearchSort.Name, SearchDirection.Ascending, Strings.BrowseSortNameAToZ),
        (SearchSort.Name, SearchDirection.Descending, Strings.BrowseSortNameZToA),
    ];

    /// <summary>
    /// How recently the mods a search finds were updated: any time, or in the last day, week or month.
    /// </summary>
    private static readonly TimeWindowChoice[] TimeWindows =
    [
        new(null, Strings.BrowseUpdatedAnyTime),
        new(TimeSpan.FromDays(1), Strings.BrowseUpdatedDay),
        new(TimeSpan.FromDays(7), Strings.BrowseUpdatedWeek),
        new(TimeSpan.FromDays(30), Strings.BrowseUpdatedMonth),
    ];

    /// <summary>
    /// How many mods a page can list: 20, 50 or 100.
    /// </summary>
    private static readonly PageSizeChoice[] PageSizes =
        [.. new[] { 20, 50, SearchQuery.MaxCount }.Select(count => new PageSizeChoice(count, Text.Format(Strings.BrowsePerPage, count)))];

    /// <summary>
    /// Every choice, as before a game is chosen, or for a site that can't search.
    /// </summary>
    public static BrowseOffer Every { get; } = For(capabilities: null, SiteWords.None);

    /// <summary>
    /// What Browse offers for a site whose search can do as <paramref name="capabilities"/> say, every choice when null;
    /// its orders by approval named in its words.
    /// </summary>
    public static BrowseOffer For(SearchCapabilities? capabilities, SiteWords words)
    {
        ArgumentNullException.ThrowIfNull(words);

        string approvals = words.Approvals ?? Strings.BrowseApprovalsUnnamed;
        SortChoice[] every =
        [
            .. Orders.Select(order => new SortChoice(
                order.Sort,
                order.Direction,
                order.Name ?? Text.Format(order.Direction == SearchDirection.Descending ? Strings.BrowseSortMostApproved : Strings.BrowseSortLeastApproved, approvals))),
        ];
        if (capabilities is null)
        {
            return new BrowseOffer(
                every,
                every[0],
                TimeWindows,
                PageSizes,
                SearchFilters.Text | SearchFilters.Author | SearchFilters.Category | SearchFilters.UpdatedSince | SearchFilters.AdultContent);
        }

        SortChoice[] offered = [.. every.Where(choice => capabilities.Has(new SearchOrder(choice.Sort, choice.Direction)))];
        SortChoice[] sorts = offered.Length > 0 ? offered : every;
        SearchOrder own = capabilities.Orders[0];
        PageSizeChoice[] sizes = [.. PageSizes.Where(size => size.Count <= capabilities.MaxCount)];
        return new BrowseOffer(
            sorts,
            sorts.FirstOrDefault(choice => choice.Sort == own.Sort && choice.Direction == own.Direction) ?? sorts[0],
            capabilities.Has(SearchFilters.UpdatedSince) ? TimeWindows : [TimeWindows[0]],
            sizes.Length > 0 ? sizes : [new PageSizeChoice(capabilities.MaxCount, Text.Format(Strings.BrowsePerPage, capabilities.MaxCount))],
            capabilities.Filters);
    }
}
