namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// What a search is narrowed and ordered by: what the filter card holds, what's applied, and what each game keeps while
/// the app runs.
/// </summary>
/// <param name="Sort">The order of the results.</param>
/// <param name="Window">How recently the mods were updated.</param>
/// <param name="Category">The category; every category's choice for every one.</param>
/// <param name="Author">Only mods by this author; empty for any.</param>
/// <param name="IncludeAdultContent">Whether mods with adult content are included.</param>
/// <param name="PageSize">How many mods a page lists.</param>
internal sealed record BrowseFilterSet(
    SortChoice Sort,
    TimeWindowChoice Window,
    CategoryChoice Category,
    string Author,
    bool IncludeAdultContent,
    PageSizeChoice PageSize);
