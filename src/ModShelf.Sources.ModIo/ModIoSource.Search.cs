using System.Text;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IModSearch"/> half: a game's mods in mod.io's orders, narrowed by words, author, tag and when they
/// were updated, all at once. mod.io marks mods with explicit content but can't leave them out, so ModShelf does. Paid
/// mods are left out, as mod.io leaves them out unless asked.
/// </summary>
public sealed partial class ModIoSource : IModSearch
{
    /// <summary>
    /// How many mods a page holds at most.
    /// </summary>
    internal const int MaxPageSize = 50;

    private static readonly SearchOrder Newest = new(SearchSort.Newest, SearchDirection.Descending);
    private static readonly SearchOrder Oldest = new(SearchSort.Newest, SearchDirection.Ascending);
    private static readonly SearchOrder RecentlyUpdated = new(SearchSort.Updated, SearchDirection.Descending);
    private static readonly SearchOrder MostDownloaded = new(SearchSort.Downloads, SearchDirection.Descending);
    private static readonly SearchOrder NameAToZ = new(SearchSort.Name, SearchDirection.Ascending);
    private static readonly SearchOrder NameZToA = new(SearchSort.Name, SearchDirection.Descending);

    /// <summary>
    /// The newest first, as mod.io lists a game's mods, and its other orders; mod.io doesn't order a search by how well
    /// the words match. It narrows by words, author, tag and when mods were updated, at once, and marks mods with explicit
    /// content without leaving them out.
    /// </summary>
    public SearchCapabilities Capabilities { get; } = new(
        [Newest, Oldest, RecentlyUpdated, MostDownloaded, NameAToZ, NameZToA],
        SearchFilters.Text | SearchFilters.Author | SearchFilters.Category | SearchFilters.UpdatedSince,
        MaxPageSize,
        marksAdultContent: true);

    /// <remarks>
    /// The best match first (<see cref="SearchSort.Relevance"/>), as asked when no order is chosen, is mod.io's own
    /// order: the newest first.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The game isn't mod.io's, or the search asks for an order mod.io doesn't sort by, or more mods than a page holds.
    /// </exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused; with 403 when the game is closed to apps.</exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        long gameId = GameId(game);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Count > MaxPageSize)
        {
            throw new ArgumentException($"A page of mod.io's mods holds {MaxPageSize} at most.", nameof(query));
        }

        StringBuilder filters = new();
        if (query.Text is { } words)
        {
            filters.Append("&_q=").Append(Escaped(words));
        }

        if (query.Author is { } author)
        {
            filters.Append("&submitted_by_display_name=").Append(Escaped(author));
        }

        if (query.Category is { } category)
        {
            filters.Append("&tags=").Append(Escaped(category));
        }

        if (query.UpdatedSince is { } since)
        {
            // In whole seconds, as mod.io counts, not before the time asked.
            filters.Append("&date_updated-min=").Append(Number((since.ToUnixTimeMilliseconds() + 999) / 1000));
        }

        ModIoPage<ModIoMod> found = await ModsPageAsync(gameId, Sort(query, nameof(query)), query.Offset, query.Count, filters.ToString(), cancellationToken)
            .ConfigureAwait(false);
        return new SearchPage(OfGame(game, gameId, found.Data, query.Category), found.ResultTotal, query.Offset);
    }

    /// <summary>
    /// mod.io's name for the order: a column, after a minus for the most first.
    /// </summary>
    private static string Sort(SearchQuery query, string paramName) => (query.Sort, query.Direction) switch
    {
        (SearchSort.Newest or SearchSort.Relevance, SearchDirection.Descending) => "-date_live",
        (SearchSort.Newest, SearchDirection.Ascending) => "date_live",
        (SearchSort.Updated, SearchDirection.Descending) => "-date_updated",
        (SearchSort.Downloads, SearchDirection.Descending) => "-downloads_total",
        (SearchSort.Name, SearchDirection.Ascending) => "name",
        (SearchSort.Name, SearchDirection.Descending) => "-name",
        _ => throw new ArgumentException($"mod.io can't list a game's mods by {query.Sort} that way round.", paramName),
    };

    /// <summary>
    /// The mods as ModShelf describes them; any of another game left out.
    /// </summary>
    /// <param name="category">The category searched for, to name it as each mod's.</param>
    private static IEnumerable<ModInfo> OfGame(GameRef game, long gameId, IEnumerable<ModIoMod>? mods, string? category = null)
        => (mods ?? [])
            .Where(mod => mod is { Id: > 0 } && (mod.GameId == gameId || mod.GameId == 0))
            .Select(mod => ModIoMods.ToModInfo(game, mod, category));

    /// <summary>
    /// A page of the game's mods, in the order, narrowed by the filters, each written as <c>&amp;name=value</c>.
    /// </summary>
    private Task<ModIoPage<ModIoMod>> ModsPageAsync(long gameId, string sort, int offset, int count, string filters, CancellationToken cancellationToken)
        => _api.GetAsync<ModIoPage<ModIoMod>>(
            GameApi(gameId, $"games/{Number(gameId)}/mods?_sort={sort}&_limit={Number(count)}&_offset={Number(offset)}{filters}"),
            cancellationToken);
}
