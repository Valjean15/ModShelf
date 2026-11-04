namespace ModShelf.Application.State;

/// <summary>
/// What the desktop app remembers for the user between runs, apart from the settings: where its window was, whether the
/// menu was collapsed, the screen last open, and Browse's recent searches. Each left null hasn't been remembered yet.
/// </summary>
/// <remarks>
/// A value the user sets on purpose is a setting, in the settings; one the app remembers for them is state, here.
/// </remarks>
public sealed record UiState
{
    /// <summary>
    /// How many of Browse's searches are remembered: 10.
    /// </summary>
    public const int MaxBrowseSearches = 10;

    /// <summary>
    /// Nothing remembered yet.
    /// </summary>
    public static UiState None { get; } = new();

    /// <summary>
    /// Where the window was, and how big.
    /// </summary>
    public WindowPlacement? Window { get; init; }

    /// <summary>
    /// Whether the menu was collapsed to its icons.
    /// </summary>
    public bool? MenuCollapsed { get; init; }

    /// <summary>
    /// The screen last open, by its name, such as "browse". A name no screen has is passed over.
    /// </summary>
    public string? Screen
    {
        get;
        init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Browse's recent searches, the newest first: at most <see cref="MaxBrowseSearches"/>, each once, case aside, with
    /// none empty.
    /// </summary>
    /// <exception cref="ArgumentNullException">The list is null.</exception>
    public IReadOnlyList<string> BrowseSearches
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Searches(value);
        }
    } = [];

    /// <summary>
    /// The state with <paramref name="text"/> as Browse's newest search: an older one the same, case aside, moves up, and
    /// the oldest goes once there are more than <see cref="MaxBrowseSearches"/>. Empty text changes nothing.
    /// </summary>
    public UiState WithBrowseSearch(string? text)
        => string.IsNullOrWhiteSpace(text) ? this : this with { BrowseSearches = [text, .. BrowseSearches] };

    public bool Equals(UiState? other)
        => other is not null
            && Window == other.Window
            && MenuCollapsed == other.MenuCollapsed
            && Screen == other.Screen
            && BrowseSearches.SequenceEqual(other.BrowseSearches);

    public override int GetHashCode() => HashCode.Combine(Window, MenuCollapsed, Screen, BrowseSearches.Count);

    private static string[] Searches(IEnumerable<string> searches)
        => [
            .. searches
                .Where(search => !string.IsNullOrWhiteSpace(search))
                .Select(search => search.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBrowseSearches),
        ];
}
