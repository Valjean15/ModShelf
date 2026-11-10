namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// Finds the site's games by name, for a site with too many games to list them all at once, as GameBanana's take nearly
/// a hundred requests: the app asks it as a name is typed, and never reads the whole list. A source implements it only
/// when its site has such a search, beside <see cref="IModCatalog"/>.
/// </summary>
/// <remarks>
/// It reports a refused account as <see cref="Accounts.AuthenticationFailedException"/> and anything else the site
/// refuses as <see cref="HttpRequestException"/>.
/// </remarks>
public interface IGameSearch : IModSource
{
    /// <param name="words">What was typed, such as "persona 5": words in any order, each perhaps the start of one.</param>
    /// <returns>
    /// Games whose names may have the words, as the site's own search finds them, at most as many as one search gives:
    /// the app narrows them with its own matching, so more may come than match, and a game can be missing that the
    /// site's search doesn't find for them.
    /// </returns>
    /// <exception cref="ArgumentException">The site can't search by the words, such as when none is long enough.</exception>
    Task<IReadOnlyList<GameInfo>> FindGamesAsync(string words, CancellationToken cancellationToken);
}
