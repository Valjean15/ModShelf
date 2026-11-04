using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Checks a search against what the site's search can do, so one asking more is refused, saying what the site can,
/// rather than sent and refused or quietly ignored there.
/// </summary>
internal static class SearchChecks
{
    private static readonly (SearchFilters Filter, string Name)[] Filters =
    [
        (SearchFilters.Text, "words"),
        (SearchFilters.Author, "author"),
        (SearchFilters.Category, "category"),
        (SearchFilters.UpdatedSince, "when mods were updated"),
    ];

    /// <summary>
    /// What <paramref name="query"/> asks of a search, of the filters a site may lack. Including adult content isn't
    /// among them: a site without it has none to include.
    /// </summary>
    public static SearchFilters FiltersOf(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return (query.Text is null ? SearchFilters.None : SearchFilters.Text)
            | (query.Author is null ? SearchFilters.None : SearchFilters.Author)
            | (query.Category is null ? SearchFilters.None : SearchFilters.Category)
            | (query.UpdatedSince is null ? SearchFilters.None : SearchFilters.UpdatedSince);
    }

    /// <summary>
    /// Throws unless the site's search can do all <paramref name="query"/> asks.
    /// </summary>
    /// <param name="site">The site's name, such as "Nexus Mods".</param>
    /// <exception cref="ArgumentException">It can't, saying what it can.</exception>
    public static void Ensure(SearchCapabilities capabilities, SearchQuery query, string site)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(site);

        if (Problem(capabilities, query, site) is { } problem)
        {
            throw new ArgumentException(problem, nameof(query));
        }
    }

    private static string? Problem(SearchCapabilities capabilities, SearchQuery query, string site)
    {
        string[] lacking = [.. Filters.Where(filter => FiltersOf(query).HasFlag(filter.Filter) && !capabilities.Has(filter.Filter)).Select(filter => filter.Name)];
        if (lacking.Length > 0)
        {
            string[] able = [.. Filters.Where(filter => capabilities.Has(filter.Filter)).Select(filter => filter.Name)];
            return able.Length == 0
                ? $"{site} can't search by {List(lacking, "or")}: it lists a game's mods without narrowing them."
                : $"{site} can't search by {List(lacking, "or")}. It can search by {List(able, "and")}.";
        }

        SearchOrder order = new(query.Sort, query.Direction);
        if (!capabilities.Has(order))
        {
            if (!capabilities.Has(query.Sort))
            {
                string[] sorts = [.. capabilities.Orders.Select(known => known.Sort).Distinct().Select(sort => sort.ToString())];
                return $"{site} can't sort by {query.Sort}. It can sort by {List(sorts, "and", by: false)}.";
            }

            return query.Direction == SearchDirection.Ascending
                ? $"{site} can sort by {query.Sort} only with the most first."
                : $"{site} can sort by {query.Sort} only with the least first.";
        }

        return query.Count > capabilities.MaxCount ? $"{site} lists at most {capabilities.MaxCount} mods a page." : null;
    }

    /// <summary>
    /// Such as "author or by category", or "Relevance, Downloads and Name".
    /// </summary>
    /// <param name="by">Whether each after the first is "by" one, as the first follows a "by".</param>
    private static string List(string[] items, string last, bool by = true)
    {
        string[] named = [.. items.Select((item, index) => index > 0 && by ? $"by {item}" : item)];
        return named.Length == 1 ? named[0] : $"{string.Join(", ", named[..^1])} {last} {named[^1]}";
    }
}
