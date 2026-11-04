namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// An order a search's results can come in: a sort and which end of it comes first, such as the most downloaded first.
/// </summary>
public readonly record struct SearchOrder
{
    /// <exception cref="ArgumentOutOfRangeException">The sort or direction is unknown.</exception>
    public SearchOrder(SearchSort sort, SearchDirection direction)
    {
        if (!Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown sort.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction.");
        }

        Sort = sort;
        Direction = direction;
    }

    public SearchSort Sort { get; }

    public SearchDirection Direction { get; }

    /// <summary>
    /// The sort with the most first, and with the least first.
    /// </summary>
    public static IEnumerable<SearchOrder> BothWays(SearchSort sort)
        => [new(sort, SearchDirection.Descending), new(sort, SearchDirection.Ascending)];
}
