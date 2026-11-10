using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IModSearch"/> half: a game's mods, 50 a page, in GameBanana's orders, narrowed by category or author;
/// or a search by words, which GameBanana answers 15 a page, and orders its own way. GameBanana marks mods rated for
/// adults but can't leave them out, so ModShelf does.
/// </summary>
public sealed partial class GameBananaSource : IModSearch
{
    /// <summary>
    /// How many mods a page of a game's mods holds at most: the most GameBanana gives.
    /// </summary>
    internal const int MaxPageSize = 50;

    /// <summary>
    /// How many results a page of a search by words holds: always 15.
    /// </summary>
    internal const int WordsPageSize = 15;

    private static readonly SearchOrder Newest = new(SearchSort.Newest, SearchDirection.Descending);
    private static readonly SearchOrder Oldest = new(SearchSort.Newest, SearchDirection.Ascending);
    private static readonly SearchOrder RecentlyUpdated = new(SearchSort.Updated, SearchDirection.Descending);
    private static readonly SearchOrder MostLiked = new(SearchSort.Endorsements, SearchDirection.Descending);
    private static readonly SearchOrder MostDownloaded = new(SearchSort.Downloads, SearchDirection.Descending);
    private static readonly SearchOrder NameAToZ = new(SearchSort.Name, SearchDirection.Ascending);
    private static readonly SearchOrder NameZToA = new(SearchSort.Name, SearchDirection.Descending);
    private static readonly SearchOrder BestMatch = new(SearchSort.Relevance, SearchDirection.Descending);

    /// <summary>
    /// Best match first, as GameBanana orders a search by words, which without words is the newest, as it lists a game's
    /// mods; and its other orders. It narrows by words, author or category, one at a time, and marks mods rated for
    /// adults without leaving them out.
    /// </summary>
    public SearchCapabilities Capabilities { get; } = new(
        [BestMatch, Newest, Oldest, RecentlyUpdated, MostLiked, MostDownloaded, NameAToZ, NameZToA],
        SearchFilters.Text | SearchFilters.Author | SearchFilters.Category,
        MaxPageSize,
        marksAdultContent: true);

    /// <exception cref="ArgumentException">
    /// The game isn't GameBanana's; the search asks for words with an author or a category, which GameBanana can't do at
    /// once, words in an order GameBanana doesn't sort them by, or a category the game doesn't have.
    /// </exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused.</exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        string gameId = GameId(game);
        ArgumentNullException.ThrowIfNull(query);
        if (query.UpdatedSince is not null)
        {
            throw new ArgumentException("GameBanana can't search by when mods were updated.", nameof(query));
        }

        return query.Text is { } words
            ? await SearchWordsAsync(game, gameId, words, query, cancellationToken).ConfigureAwait(false)
            : await ListModsAsync(game, gameId, query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GameBanana's name for a game's mods' order.
    /// </summary>
    private static string IndexSort(SearchQuery query, string paramName) => (query.Sort, query.Direction) switch
    {
        (SearchSort.Newest, SearchDirection.Descending) or (SearchSort.Relevance, _) => "Generic_Newest",
        (SearchSort.Newest, SearchDirection.Ascending) => "Generic_Oldest",
        (SearchSort.Updated, SearchDirection.Descending) => "Generic_LatestModified",
        (SearchSort.Endorsements, SearchDirection.Descending) => "Generic_MostLiked",
        (SearchSort.Downloads, SearchDirection.Descending) => "Generic_MostDownloaded",
        (SearchSort.Name, SearchDirection.Ascending) => "Generic_Alphabetically",
        (SearchSort.Name, SearchDirection.Descending) => "Generic_ReverseAlphabetically",
        _ => throw new ArgumentException($"GameBanana can't list a game's mods by {query.Sort} that way round.", paramName),
    };

    /// <summary>
    /// GameBanana's name for a search by words' order.
    /// </summary>
    private static string WordsOrder(SearchQuery query, string paramName) => (query.Sort, query.Direction) switch
    {
        (SearchSort.Relevance, SearchDirection.Descending) => "best_match",
        (SearchSort.Newest, SearchDirection.Descending) => "date",
        (SearchSort.Updated, SearchDirection.Descending) => "udate",
        (SearchSort.Endorsements or SearchSort.Downloads, SearchDirection.Descending) => "popularity",
        _ => throw new ArgumentException(
            "GameBanana sorts a search by words by best match, popularity, newest or recently updated only.", paramName),
    };

    private static string Escaped(string text) => Uri.EscapeDataString(text);

    private static string Number(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A page of the game's mods: GameBanana's pages of the page's size, the one the page starts on and, when it starts
    /// part-way into one, the next.
    /// </summary>
    private async Task<SearchPage> ListModsAsync(GameRef game, string gameId, SearchQuery query, CancellationToken cancellationToken)
    {
        string sort = IndexSort(query, nameof(query));
        string filter = string.Empty;
        if (query.Author is { } author)
        {
            if (await MemberIdAsync(author, cancellationToken).ConfigureAwait(false) is not { } member)
            {
                return new SearchPage([], 0, query.Offset);
            }

            filter = $"&{Escaped("_aFilters[Generic_Submitter]")}={Number(member)}";
        }

        if (query.Category is { } category)
        {
            filter += $"&{Escaped("_aFilters[Generic_Category]")}={Number(await CategoryIdAsync(game, gameId, category, cancellationToken).ConfigureAwait(false))}";
        }

        int first = (query.Offset / query.Count) + 1;
        int skip = query.Offset % query.Count;
        List<GameBananaMod> mods = [];
        int total = 0;
        for (int page = first; page <= first + (skip > 0 ? 1 : 0); page++)
        {
            GameBananaPage<GameBananaMod> found = await _api.GetAsync<GameBananaPage<GameBananaMod>>(
                    $"apiv11/Mod/Index?_nPage={Number(page)}&_nPerpage={Number(query.Count)}&_sSort={sort}&{Escaped("_aFilters[Generic_Game]")}={gameId}{filter}",
                    cancellationToken)
                .ConfigureAwait(false);
            total = page == first ? found.Metadata?.RecordCount ?? 0 : total;
            mods.AddRange((found.Records ?? []).Where(mod => mod is not null));
            if (found.Metadata?.IsComplete != false)
            {
                break;
            }
        }

        return new SearchPage(OfGame(game, gameId, mods.Skip(skip).Take(query.Count)), total, query.Offset);
    }

    /// <summary>
    /// A page of a search by words: the pages of 15 GameBanana answers it with that the page covers.
    /// </summary>
    private async Task<SearchPage> SearchWordsAsync(GameRef game, string gameId, string words, SearchQuery query, CancellationToken cancellationToken)
    {
        if (query.Author is not null || query.Category is not null)
        {
            throw new ArgumentException("GameBanana can't search by words and by author or category at once.", nameof(query));
        }

        string order = WordsOrder(query, nameof(query));
        int first = (query.Offset / WordsPageSize) + 1;
        int last = ((query.Offset + query.Count - 1) / WordsPageSize) + 1;
        List<GameBananaMod> mods = [];
        int total = 0;
        for (int page = first; page <= last; page++)
        {
            GameBananaPage<GameBananaMod> found = await _api.GetAsync<GameBananaPage<GameBananaMod>>(
                    $"apiv11/Util/Search/Results?_sModelName=Mod&_sOrder={order}&_idGameRow={gameId}&_sSearchString={Escaped(words)}&_nPage={Number(page)}",
                    cancellationToken)
                .ConfigureAwait(false);
            total = page == first ? found.Metadata?.RecordCount ?? 0 : total;
            mods.AddRange((found.Records ?? []).Where(mod => mod is not null));
            if (found.Metadata?.IsComplete != false)
            {
                break;
            }
        }

        int skip = query.Offset - ((first - 1) * WordsPageSize);
        return new SearchPage(OfGame(game, gameId, mods.Skip(skip).Take(query.Count)), total, query.Offset);
    }

    /// <summary>
    /// The mods as ModShelf describes them, each with its top category; any GameBanana lists under another game left out.
    /// </summary>
    private IEnumerable<ModInfo> OfGame(GameRef game, string gameId, IEnumerable<GameBananaMod> mods)
    {
        foreach (GameBananaMod mod in mods)
        {
            if (mod.Game is { } listed && Number(listed.Id) != gameId)
            {
                LogModOfAnotherGame(_logger, mod.Id, gameId, listed.Id);
                continue;
            }

            yield return GameBananaMods.ToModInfo(game, mod, mod.RootCategory?.Name);
        }
    }

    /// <summary>
    /// The member of that name, as GameBanana's member search finds them; null when it finds no one of that name.
    /// </summary>
    private async Task<long?> MemberIdAsync(string name, CancellationToken cancellationToken)
    {
        GameBananaPage<GameBananaMember> found = await _api.GetAsync<GameBananaPage<GameBananaMember>>(
                $"apiv11/Util/Search/Results?_sModelName=Member&_sSearchString={Escaped(name.Trim())}",
                cancellationToken)
            .ConfigureAwait(false);
        return (found.Records ?? []).FirstOrDefault(member => member is not null && string.Equals(member.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// The ID of the game's top category of that name, read with the game when not yet.
    /// </summary>
    /// <exception cref="ArgumentException">The game has no such category.</exception>
    private async Task<long> CategoryIdAsync(GameRef game, string gameId, string name, CancellationToken cancellationToken)
    {
        if (!_categories.ContainsKey(gameId))
        {
            await GetGameAsync(game, cancellationToken).ConfigureAwait(false);
        }

        return _categories.TryGetValue(gameId, out IReadOnlyDictionary<string, long>? categories) && categories.TryGetValue(name.Trim(), out long id)
            ? id
            : throw new ArgumentException($"The game has no category '{name}' on GameBanana.", nameof(name));
    }
}
