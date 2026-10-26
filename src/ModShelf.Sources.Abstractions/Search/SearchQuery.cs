namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// What to search a game's mods for, and which page of the results to return.
/// </summary>
public sealed record SearchQuery
{
    /// <summary>
    /// The most mods one page can hold.
    /// </summary>
    public const int MaxCount = 100;

    /// <exception cref="ArgumentOutOfRangeException">
    /// The sort is unknown, the offset negative, or the count not between 1 and <see cref="MaxCount"/>.
    /// </exception>
    public SearchQuery(
        string? text = null,
        SearchSort sort = SearchSort.Relevance,
        int offset = 0,
        int count = 20,
        string? author = null,
        bool includeAdultContent = false)
    {
        if (!Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown sort.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxCount);

        Text = Blank(text);
        Sort = sort;
        Offset = offset;
        Count = count;
        Author = Blank(author);
        IncludeAdultContent = includeAdultContent;
    }

    /// <summary>
    /// Words to look for in the mods' names, or null for every mod of the game.
    /// </summary>
    public string? Text { get; }

    public SearchSort Sort { get; }

    /// <summary>
    /// How many results to skip: the page's start.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// How many results the page holds at most.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Only mods by this author, as the site credits them, or null for any author.
    /// </summary>
    public string? Author { get; }

    /// <summary>
    /// Whether mods with adult content are included. They're left out unless asked for.
    /// </summary>
    public bool IncludeAdultContent { get; }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
