using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The choices of Browse's toolbar that a game's own settings can start it on: the orders, how recently the mods were
/// updated, and the page sizes. The first of each is Browse's own.
/// </summary>
internal static class BrowseChoices
{
    /// <summary>
    /// The orders a search's results can come in.
    /// </summary>
    public static IReadOnlyList<SortChoice> Sorts { get; } =
    [
        new(SearchSort.Relevance, SearchDirection.Descending, Strings.BrowseSortBestMatch),
        new(SearchSort.Endorsements, SearchDirection.Descending, Strings.BrowseSortMostEndorsed),
        new(SearchSort.Endorsements, SearchDirection.Ascending, Strings.BrowseSortLeastEndorsed),
        new(SearchSort.Downloads, SearchDirection.Descending, Strings.BrowseSortMostDownloaded),
        new(SearchSort.Downloads, SearchDirection.Ascending, Strings.BrowseSortLeastDownloaded),
        new(SearchSort.Updated, SearchDirection.Descending, Strings.BrowseSortRecentlyUpdated),
        new(SearchSort.Updated, SearchDirection.Ascending, Strings.BrowseSortLongestSinceUpdated),
        new(SearchSort.Newest, SearchDirection.Descending, Strings.BrowseSortNewest),
        new(SearchSort.Newest, SearchDirection.Ascending, Strings.BrowseSortOldest),
        new(SearchSort.UniqueDownloads, SearchDirection.Descending, Strings.BrowseSortMostUniqueDownloads),
        new(SearchSort.Size, SearchDirection.Descending, Strings.BrowseSortLargest),
        new(SearchSort.Size, SearchDirection.Ascending, Strings.BrowseSortSmallest),
        new(SearchSort.Name, SearchDirection.Ascending, Strings.BrowseSortNameAToZ),
        new(SearchSort.Name, SearchDirection.Descending, Strings.BrowseSortNameZToA),
    ];

    /// <summary>
    /// How recently the mods a search finds were updated: any time, or in the last day, week or month.
    /// </summary>
    public static IReadOnlyList<TimeWindowChoice> TimeWindows { get; } =
    [
        new(null, Strings.BrowseUpdatedAnyTime),
        new(TimeSpan.FromDays(1), Strings.BrowseUpdatedDay),
        new(TimeSpan.FromDays(7), Strings.BrowseUpdatedWeek),
        new(TimeSpan.FromDays(30), Strings.BrowseUpdatedMonth),
    ];

    /// <summary>
    /// How many mods a page can list: 20, 50 or 100.
    /// </summary>
    public static IReadOnlyList<PageSizeChoice> PageSizes { get; } =
        [.. new[] { 20, 50, SearchQuery.MaxCount }.Select(count => new PageSizeChoice(count, Text.Format(Strings.BrowsePerPage, count)))];

    /// <summary>
    /// The order <paramref name="sort"/> names, going as <paramref name="direction"/> says, or the order's own way;
    /// null when there's none such.
    /// </summary>
    public static SortChoice? SortOf(SearchSort sort, SearchDirection? direction)
        => Sorts.FirstOrDefault(choice => choice.Sort == sort && (direction is null || choice.Direction == direction));
}
