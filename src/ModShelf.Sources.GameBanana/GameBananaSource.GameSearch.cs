using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IGameSearch"/> half: GameBanana's games found by name, as its own search finds them, so the app never
/// reads its nearly hundred pages of games for a list to pick from (ADR 0088).
/// </summary>
public sealed partial class GameBananaSource : IGameSearch
{
    /// <summary>
    /// The fewest letters or digits a word GameBanana finds games by has.
    /// </summary>
    internal const int ShortestWord = 3;

    /// <summary>
    /// How many pages of 50 a search of games by name reads at most: typing more of the name finds the rest.
    /// </summary>
    internal const int FindPages = 2;

    /// <summary>
    /// How many searches are kept while the app runs, each word's once; past that, they're asked again.
    /// </summary>
    internal const int FoundKept = 200;

    // Each word's games, once found: typing more of a name asks GameBanana for its longest word again and again.
    private readonly ConcurrentDictionary<string, IReadOnlyList<GameInfo>> _found = new(StringComparer.Ordinal);

    /// <summary>
    /// GameBanana's search finds a game whose name has a word as typed, one word at a time, and none for a word shorter
    /// than 3: it's asked for the longest word typed, lowercase, 100 games at most, and the app matches the rest. Each
    /// word's answer is kept while the app runs.
    /// </summary>
    /// <exception cref="ArgumentException">No word typed has 3 letters or digits or more.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<GameInfo>> FindGamesAsync(string words, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        string word = LongestWord(words)
            ?? throw new ArgumentException($"GameBanana finds games by a word of {ShortestWord} letters or more.", nameof(words));
        if (_found.TryGetValue(word, out IReadOnlyList<GameInfo>? kept))
        {
            return kept;
        }

        List<GameInfo> games = [];
        for (int page = 1; page <= FindPages; page++)
        {
            GameBananaPage<GameBananaGame> found = await _api.GetAsync<GameBananaPage<GameBananaGame>>(
                    $"apiv11/Util/Game/NameMatch?_sName={Uri.EscapeDataString(word)}&_nPerpage={GamesPerPage}&_nPage={page.ToString(CultureInfo.InvariantCulture)}",
                    cancellationToken)
                .ConfigureAwait(false);
            games.AddRange(
                (found.Records ?? [])
                    .Where(game => game is not null && !string.IsNullOrWhiteSpace(game.Name))
                    .Select(game => new GameInfo(
                        GameOf(game.Id),
                        game.Name!.Trim(),
                        GameBananaMods.WebLink(game.ProfileUrl),
                        coverUrl: Picture(game.BannerUrl),
                        iconUrl: Picture(game.IconUrl))));
            if (found.Metadata?.IsComplete != false)
            {
                break;
            }
        }

        IReadOnlyList<GameInfo> result = [.. games.DistinctBy(game => game.Game)];
        if (_found.Count < FoundKept)
        {
            _found[word] = result;
        }

        return result;
    }

    /// <summary>
    /// The longest word typed of 3 letters or digits or more, lowercase; the first of the longest. Null for none.
    /// </summary>
    internal static string? LongestWord(string words)
        => NotWord().Split(words)
            .Where(word => word.Length >= ShortestWord)
            .OrderByDescending(word => word.Length)
            .Select(word => word.ToLowerInvariant())
            .FirstOrDefault();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotWord();
}
