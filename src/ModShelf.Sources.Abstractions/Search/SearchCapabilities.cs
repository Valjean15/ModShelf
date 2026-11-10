namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// What a site's search can do: the orders it lists mods in, what it narrows them by, and how many a page can hold.
/// ModShelf asks it nothing more: a search that would is refused before it's sent, and the screens offer only these.
/// </summary>
public sealed record SearchCapabilities
{
    private const SearchFilters Every = SearchFilters.Text | SearchFilters.Author | SearchFilters.Category
        | SearchFilters.UpdatedSince | SearchFilters.AdultContent;

    /// <param name="orders">Its orders, the one it uses unless asked otherwise first; repeats are left out.</param>
    /// <param name="filters">What it can narrow its results by.</param>
    /// <param name="maxCount">The most mods a page can hold: from 1 to <see cref="SearchQuery.MaxCount"/>.</param>
    /// <param name="marksAdultContent">
    /// Whether it marks adult mods though it can't leave them out, as <see cref="SearchFilters.AdultContent"/> does: then
    /// ModShelf leaves them out itself unless asked. A site with the filter marks them anyway.
    /// </param>
    /// <exception cref="ArgumentException">There's no order.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A filter is unknown, or the page size out of range.</exception>
    public SearchCapabilities(IEnumerable<SearchOrder> orders, SearchFilters filters, int maxCount, bool marksAdultContent = false)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if ((filters & ~Every) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filters), filters, "Unknown filter.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCount, SearchQuery.MaxCount);

        Orders = [.. orders.Distinct()];
        if (Orders.Count == 0)
        {
            throw new ArgumentException("A search lists its mods in one order at least.", nameof(orders));
        }

        Filters = filters;
        MaxCount = maxCount;
        MarksAdultContent = marksAdultContent || Has(SearchFilters.AdultContent);
    }

    /// <summary>
    /// The orders it lists mods in, the one it uses unless asked otherwise first.
    /// </summary>
    public IReadOnlyList<SearchOrder> Orders { get; }

    /// <summary>
    /// What it can narrow its results by.
    /// </summary>
    public SearchFilters Filters { get; }

    /// <summary>
    /// The most mods a page can hold. A page holds as many as asked, up to this, while more match.
    /// </summary>
    public int MaxCount { get; }

    /// <summary>
    /// Whether it marks adult mods (<see cref="Catalog.ModInfo.HasAdultContent"/>): always when it can leave them out
    /// (<see cref="SearchFilters.AdultContent"/>); otherwise ModShelf leaves them out itself, after each page, unless
    /// asked. A site that doesn't marks none.
    /// </summary>
    public bool MarksAdultContent { get; }

    /// <summary>
    /// Whether it lists mods in that order.
    /// </summary>
    public bool Has(SearchOrder order) => Orders.Contains(order);

    /// <summary>
    /// Whether it lists mods by that sort, either way round.
    /// </summary>
    public bool Has(SearchSort sort) => Orders.Any(order => order.Sort == sort);

    /// <summary>
    /// Whether it can narrow its results by every one of <paramref name="filters"/>.
    /// </summary>
    public bool Has(SearchFilters filters) => (Filters & filters) == filters;

    /// <summary>
    /// Whether both say the same, the orders compared one by one rather than as lists.
    /// </summary>
    public bool Equals(SearchCapabilities? other)
        => other is not null
            && Filters == other.Filters
            && MaxCount == other.MaxCount
            && MarksAdultContent == other.MarksAdultContent
            && Orders.SequenceEqual(other.Orders);

    public override int GetHashCode() => HashCode.Combine(Filters, MaxCount, MarksAdultContent, Orders.Count);
}
