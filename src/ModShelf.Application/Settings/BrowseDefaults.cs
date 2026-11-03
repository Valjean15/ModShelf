using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Settings;

/// <summary>
/// How Browse shows a game's mods when the game is chosen; each left null takes the screen's own default. The toolbar
/// still changes them for the visit.
/// </summary>
public sealed record BrowseDefaults
{
    /// <summary>
    /// The times Browse can show the mods updated within: a day, a week and 30 days.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Windows { get; } = [TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30)];

    /// <summary>
    /// The numbers of mods Browse can show a page: 20, 50 and 100.
    /// </summary>
    public static IReadOnlyList<int> PageSizes { get; } = [20, 50, SearchQuery.MaxCount];

    /// <summary>
    /// No default: the screen's own.
    /// </summary>
    public static BrowseDefaults None { get; } = new();

    /// <summary>
    /// The order, such as <see cref="SearchSort.Updated"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The order is unknown.</exception>
    public SearchSort? Sort
    {
        get;
        init
        {
            if (value is { } sort && !Enum.IsDefined(sort))
            {
                throw new ArgumentOutOfRangeException(nameof(Sort), sort, "The order is unknown.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Which way the order goes; null for the order's own way, such as the most recently updated first.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The direction is unknown.</exception>
    public SearchDirection? Direction
    {
        get;
        init
        {
            if (value is { } direction && !Enum.IsDefined(direction))
            {
                throw new ArgumentOutOfRangeException(nameof(Direction), direction, "The direction is unknown.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Only the mods updated within this long; one of <see cref="Windows"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">It's none of <see cref="Windows"/>.</exception>
    public TimeSpan? UpdatedWithin
    {
        get;
        init
        {
            if (value is { } within && !Windows.Contains(within))
            {
                throw new ArgumentOutOfRangeException(nameof(UpdatedWithin), within, "Mods can be shown by when they were updated within a day, 7 days or 30 days.");
            }

            field = value;
        }
    }

    /// <summary>
    /// How many mods a page shows; one of <see cref="PageSizes"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">It's none of <see cref="PageSizes"/>.</exception>
    public int? PageSize
    {
        get;
        init
        {
            if (value is { } size && !PageSizes.Contains(size))
            {
                throw new ArgumentOutOfRangeException(nameof(PageSize), size, "A page can show 20, 50 or 100 mods.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Whether every default is the screen's own.
    /// </summary>
    public bool IsNone => this == None;
}
