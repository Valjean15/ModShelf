using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A search of a community's packages, as its listing gives them: deprecated packages left out, as Thunderstore's
/// website does, and adult ones unless asked.
/// </summary>
internal static class PackageSearch
{
    private static readonly StringComparer ByName = StringComparer.OrdinalIgnoreCase;

    /// <returns>The packages that match <paramref name="query"/>, in <paramref name="order"/>, ties by full name.</returns>
    public static IEnumerable<ListedPackage> Find(IEnumerable<ListedPackage> packages, SearchQuery query, SearchOrder order)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(query);

        string[] words = query.Text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        IEnumerable<ListedPackage> found = packages.Where(package => !package.IsDeprecated
            && (query.IncludeAdultContent || !package.HasAdultContent)
            && (query.Author is not { } author || string.Equals(package.Name.Team, author.Trim(), StringComparison.OrdinalIgnoreCase))
            && (query.Category is not { } category || package.Categories.Contains(category.Trim(), ByName))
            && (query.UpdatedSince is not { } since || package.UpdatedAt >= since)
            && words.All(word => Holds(package, word)));
        return Ordered(found, order);
    }

    /// <summary>
    /// Whether the package's name, as shown or written, its team or its description holds the word, in any case.
    /// </summary>
    private static bool Holds(ListedPackage package, string word)
        => package.Name.DisplayName.Contains(word, StringComparison.OrdinalIgnoreCase)
            || package.Name.FullName.Contains(word, StringComparison.OrdinalIgnoreCase)
            || package.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private static IOrderedEnumerable<ListedPackage> Ordered(IEnumerable<ListedPackage> packages, SearchOrder order)
    {
        bool ascending = order.Direction == SearchDirection.Ascending;
        IOrderedEnumerable<ListedPackage> sorted = order.Sort switch
        {
            SearchSort.Newest => By(packages, package => package.CreatedAt, ascending),
            SearchSort.Downloads => By(packages, package => package.Downloads, ascending),
            SearchSort.Endorsements => By(packages, package => package.Likes, ascending),
            SearchSort.Size => By(packages, package => package.Newest.Size, ascending),
            SearchSort.Name => ascending
                ? packages.OrderBy(package => package.Name.DisplayName, ByName)
                : packages.OrderByDescending(package => package.Name.DisplayName, ByName),
            _ => By(packages, package => package.UpdatedAt, ascending),
        };
        return sorted.ThenBy(package => package.Name.FullName, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<ListedPackage> By<TKey>(IEnumerable<ListedPackage> packages, Func<ListedPackage, TKey> key, bool ascending)
        => ascending ? packages.OrderBy(key) : packages.OrderByDescending(key);
}
