using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Looks mods up on their sites: one from a link to it or by reference, with its game, files and changelog; what changed
/// in one of its versions; a game's mods by search; or the games a site has.
/// </summary>
public interface IModLookup
{
    /// <summary>
    /// The sources that can search their games' mods, by ID.
    /// </summary>
    IReadOnlyList<SourceId> SearchableSources { get; }

    /// <param name="link">A link to a mod or one of its files, on a site whose source has an <c>IModCatalog</c>.</param>
    /// <exception cref="Downloads.UnsupportedLinkException">
    /// No source recognizes the link, or the source that does can't describe its mods.
    /// </exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// The site couldn't be reached or refused; with 404 when there's no such mod or file.
    /// </exception>
    Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// A mod by reference, such as one a search found, as <see cref="LookUpAsync(Uri, CancellationToken)"/> describes
    /// it, without a linked file.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The mod's source isn't registered or can't describe its mods, or the source doesn't know the mod's game or ID.
    /// </exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached or refused; with 404 when there's no such mod.</exception>
    Task<ModPage> LookUpAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// What changed in a version of a mod, as the mod's changelog on its site says: the entry for exactly
    /// <paramref name="version"/>, as the site names its versions. The changelog is read from the site the first time
    /// any version of the mod is asked about, then kept while the app runs. A read that failed is tried again the next
    /// time.
    /// </summary>
    /// <param name="version">The version's label, such as a library file's or an update's.</param>
    /// <returns>
    /// The version's changes, a line each; empty when the changelog says nothing about it. Null when the mod's source
    /// isn't registered, or can't describe its mods.
    /// </returns>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached or refused; with 404 when there's no such mod.</exception>
    Task<IReadOnlyList<string>?> GetChangesAsync(ModRef modRef, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Every game the source has mods for, each once, in no particular order: a list for the user to pick a game from.
    /// It's read from the site the first time, then kept while the app runs. A read that failed is tried again the next
    /// time.
    /// </summary>
    /// <exception cref="ArgumentException">The source isn't registered, or can't describe its games.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<GameInfo>> GetGamesAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// One page of a game's mods that match <paramref name="query"/>, from the game's source.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The game's source isn't registered or can't search, or the source doesn't know the game.
    /// </exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused the search.</exception>
    Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken);
}
