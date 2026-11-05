using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Common;

/// <summary>
/// The search filters the CLI's <c>mod search</c> and the MCP server's <c>mod_search</c> take beyond Browse's boxes, read
/// the same way: a game's category by its name, and how many days back mods were updated.
/// </summary>
internal static class SearchArguments
{
    /// <summary>
    /// The most days back a search by when mods were updated looks, a hundred years.
    /// </summary>
    public const int MostDays = 36500;

    /// <summary>
    /// Only mods updated since then: <paramref name="days"/> before <paramref name="now"/>, from the start of that hour,
    /// in UTC, so the pages of one search asked within the hour find the same mods.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is below 1 or above <see cref="MostDays"/>.</exception>
    public static DateTimeOffset UpdatedSince(DateTimeOffset now, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, MostDays);

        DateTimeOffset since = now.ToUniversalTime().AddDays(-days);
        return new DateTimeOffset(since.Year, since.Month, since.Day, since.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// The category of <paramref name="game"/> that <paramref name="asked"/> names, in any case and trimmed, as the game
    /// names it.
    /// </summary>
    /// <param name="site">The site's name, such as Nexus Mods, for the refusal.</param>
    /// <exception cref="ArgumentException">The game has no such category; the message names those it has.</exception>
    public static string Category(string asked, GameInfo game, string site)
    {
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(game);

        if (game.Categories.FirstOrDefault(category => string.Equals(category, asked.Trim(), StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            return named;
        }

        throw new ArgumentException(game.Categories.Count == 0
            ? $"{game.Name} has no categories on {site}."
            : $"{game.Name} has no category '{asked.Trim()}' on {site}. Its categories: {string.Join(", ", game.Categories)}.");
    }
}
