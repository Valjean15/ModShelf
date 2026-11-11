using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The <see cref="IModSearch"/> half: a game's mods from every mod read at once, filtered and ordered here, as DEG Mods'
/// site does, since relays search by neither words nor game.
/// </summary>
public sealed partial class DegModsSource : IModSearch
{
    /// <summary>
    /// The most mods a page holds.
    /// </summary>
    internal const int MaxPageSize = 50;

    /// <summary>
    /// Newest first, as DEG Mods lists them, or oldest; recently changed; by name both ways. Words in their names, their
    /// author, when they changed, and adult content, which is left out unless asked. DEG Mods counts no downloads or
    /// approval.
    /// </summary>
    public SearchCapabilities Capabilities { get; } = new(
        [
            new SearchOrder(SearchSort.Newest, SearchDirection.Descending),
            new SearchOrder(SearchSort.Newest, SearchDirection.Ascending),
            new SearchOrder(SearchSort.Updated, SearchDirection.Descending),
            .. SearchOrder.BothWays(SearchSort.Name),
        ],
        SearchFilters.Text | SearchFilters.Author | SearchFilters.UpdatedSince | SearchFilters.AdultContent,
        MaxPageSize);

    /// <exception cref="ArgumentException">The game isn't DEG Mods', or the search asks what DEG Mods' can't.</exception>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        string key = GameKey(game);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Category is not null)
        {
            throw new ArgumentException("DEG Mods can't be searched by category. It can be searched by words, author and when mods changed.", nameof(query));
        }

        SearchOrder order = query.Sort == SearchSort.Relevance
            ? Capabilities.Orders[0]
            : new SearchOrder(query.Sort, query.Direction);
        if (!Capabilities.Has(order))
        {
            throw new ArgumentException("DEG Mods lists mods newest or oldest first, recently changed first, or by name.", nameof(query));
        }

        string? author = query.Author is { } named ? Author(named) : null;
        string[] words = query.Text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        IReadOnlyList<DegMod> mods = await _index.AllAsync(cancellationToken).ConfigureAwait(false);
        DegMod[] found =
        [
            .. Ordered(
                mods.Where(mod => ModEvents.GameKey(mod.Game) == key
                    && (query.IncludeAdultContent || !mod.IsAdult)
                    && (author is null || mod.Author == author)
                    && (query.UpdatedSince is not { } since || mod.UpdatedAt >= since)
                    && words.All(word => mod.Title.Contains(word, StringComparison.InvariantCultureIgnoreCase))),
                order),
        ];

        return new SearchPage(
            found.Skip(query.Offset).Take(Math.Min(query.Count, MaxPageSize)).Select(mod => Info(mod, ModOf(mod))),
            found.Length,
            query.Offset);
    }

    /// <summary>
    /// An author's public key, from their <c>npub1…</c>, as ModShelf names authors, or the key in hexadecimal.
    /// </summary>
    private static string Author(string named)
        => Nip19.TryReadNpub(named.Trim(), out string key) ? key
            : named.Trim() is { Length: 64 } hex && ModEvents.IsHex(hex) ? hex.ToLowerInvariant()
            : throw new ArgumentException("DEG Mods' authors are named by their key, as npub1….", nameof(named));

    private static IEnumerable<DegMod> Ordered(IEnumerable<DegMod> mods, SearchOrder order)
    {
        Func<DegMod, string> tieBreak = mod => mod.Key;
        IOrderedEnumerable<DegMod> sorted = order.Sort switch
        {
            SearchSort.Updated => mods.OrderByDescending(mod => mod.UpdatedAt),
            SearchSort.Name when order.Direction == SearchDirection.Ascending => mods.OrderBy(mod => mod.Title, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)),
            SearchSort.Name => mods.OrderByDescending(mod => mod.Title, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)),
            _ when order.Direction == SearchDirection.Ascending => mods.OrderBy(mod => mod.PublishedAt),
            _ => mods.OrderByDescending(mod => mod.PublishedAt),
        };
        return sorted.ThenBy(tieBreak, StringComparer.Ordinal);
    }
}
