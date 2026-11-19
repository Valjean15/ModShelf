using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Thunderstore.Catalog;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="IModSearch"/> half: a community's packages from its listing, filtered and ordered here, leaving out
/// deprecated packages, as Thunderstore's website does.
/// </summary>
public sealed partial class ThunderstoreSource : IModSearch
{
    /// <summary>
    /// Last updated first, as Thunderstore's website lists packages, then newest, downloads, likes, name and size, each
    /// either way round. Words in their names, team or description, their team, a category, when they were updated, and
    /// adult content, which is left out unless asked.
    /// </summary>
    public SearchCapabilities Capabilities { get; } = new(
        [
            .. SearchOrder.BothWays(SearchSort.Updated),
            .. SearchOrder.BothWays(SearchSort.Newest),
            .. SearchOrder.BothWays(SearchSort.Downloads),
            .. SearchOrder.BothWays(SearchSort.Endorsements),
            .. SearchOrder.BothWays(SearchSort.Name),
            .. SearchOrder.BothWays(SearchSort.Size),
        ],
        SearchFilters.Text | SearchFilters.Author | SearchFilters.Category | SearchFilters.UpdatedSince | SearchFilters.AdultContent,
        SearchQuery.MaxCount);

    /// <exception cref="ArgumentException">The game isn't Thunderstore's, or the search asks for an order it can't give.</exception>
    /// <exception cref="HttpRequestException">The community's listing couldn't be read: 404 for a community Thunderstore doesn't have.</exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        string community = Community(game);
        ArgumentNullException.ThrowIfNull(query);

        SearchOrder order = query.Sort == SearchSort.Relevance
            ? Capabilities.Orders[0]
            : new SearchOrder(query.Sort, query.Direction);
        if (!Capabilities.Has(order))
        {
            throw new ArgumentException(
                "Thunderstore lists packages by when they were updated or uploaded, downloads, likes, name or size.",
                nameof(query));
        }

        Listing listing = await _listings.GetAsync(community, cancellationToken).ConfigureAwait(false);
        ListedPackage[] found = [.. PackageSearch.Find(listing.Packages, query, order)];

        // A package has several categories, and is described by its first; found by one, it's described by that one.
        return new SearchPage(
            found.Skip(query.Offset).Take(query.Count).Select(package => Describe(community, package, Matching(package, query.Category))),
            found.Length,
            query.Offset);
    }

    private static string? Matching(ListedPackage package, string? category)
        => category is null ? null : package.Categories.FirstOrDefault(listed => string.Equals(listed, category.Trim(), StringComparison.OrdinalIgnoreCase));
}
