using System.Globalization;
using System.Text;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.CurseForge.Api;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="IModSearch"/> half: a game's mods by words and by class, such as Minecraft's Mods or Resource Packs,
/// in CurseForge's orders, 50 at most a page, and no further than its 10,000th mod.
/// </summary>
public sealed partial class CurseForgeSource : IModSearch
{
    // How far CurseForge's search goes: a page's start and its size together.
    private const int Furthest = 10_000;

    private const int ByPopularity = 2;
    private const int ByLastUpdated = 3;
    private const int ByName = 4;
    private const int ByTotalDownloads = 6;
    private const int ByReleasedDate = 11;

    /// <summary>
    /// CurseForge's own order first: its most popular mods.
    /// </summary>
    public SearchCapabilities Capabilities { get; } = new(
        [
            new SearchOrder(SearchSort.Relevance, SearchDirection.Descending),
            .. SearchOrder.BothWays(SearchSort.Downloads),
            .. SearchOrder.BothWays(SearchSort.Updated),
            .. SearchOrder.BothWays(SearchSort.Newest),
            .. SearchOrder.BothWays(SearchSort.Name),
        ],
        SearchFilters.Text | SearchFilters.Category,
        maxCount: 50);

    /// <exception cref="ArgumentException">
    /// The game isn't CurseForge's, or the search asks for what CurseForge can't do, or names a class the game hasn't.
    /// </exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        SearchOrder order = new(query.Sort, query.Direction);
        if (!Capabilities.Has(order) || query.Author is not null || query.UpdatedSince is not null || query.Count > Capabilities.MaxCount)
        {
            throw new ArgumentException(
                "CurseForge searches by words and by class, by popularity, downloads, when mods were updated or released, or by name, 50 mods a page at most.",
                nameof(query));
        }

        CurseForgeGame found = await GameAsync(game, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CurseForgeCategory> classes = await _games.ClassesAsync(found.Id, cancellationToken).ConfigureAwait(false);
        long? type = null;
        if (query.Category is { } category)
        {
            type = classes.FirstOrDefault(listed => string.Equals(listed.Name?.Trim(), category, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new ArgumentException($"{found.Name} has no class of mods called {category} on CurseForge.", nameof(query));
        }

        if (query.Offset >= Furthest)
        {
            return new SearchPage([], Furthest, query.Offset);
        }

        int count = Math.Min(query.Count, Furthest - query.Offset);
        StringBuilder search = new(string.Create(CultureInfo.InvariantCulture, $"v1/mods/search?gameId={found.Id}"));
        if (type is { } classId)
        {
            search.Append(CultureInfo.InvariantCulture, $"&classId={classId}");
        }

        if (query.Text is { } text)
        {
            search.Append("&searchFilter=").Append(Uri.EscapeDataString(text));
        }

        search.Append(CultureInfo.InvariantCulture, $"&sortField={SortField(query.Sort)}&sortOrder={(query.Direction == SearchDirection.Ascending ? "asc" : "desc")}");
        search.Append(CultureInfo.InvariantCulture, $"&index={query.Offset}&pageSize={count}");
        CurseForgeAnswer<IReadOnlyList<CurseForgeMod>> answer = await _api
            .GetAsync<IReadOnlyList<CurseForgeMod>>(search.ToString(), cancellationToken)
            .ConfigureAwait(false);
        ModInfo[] mods = [.. (answer.Data ?? []).Where(mod => mod.GameId == found.Id).Select(mod => ModInfoOf(mod, found, classes))];
        int total = (int)Math.Min(answer.Pagination?.TotalCount ?? query.Offset + mods.Length, Furthest);
        return new SearchPage(mods, Math.Max(total, query.Offset + mods.Length), query.Offset);
    }

    private static int SortField(SearchSort sort) => sort switch
    {
        SearchSort.Downloads => ByTotalDownloads,
        SearchSort.Updated => ByLastUpdated,
        SearchSort.Newest => ByReleasedDate,
        SearchSort.Name => ByName,
        _ => ByPopularity,
    };
}
