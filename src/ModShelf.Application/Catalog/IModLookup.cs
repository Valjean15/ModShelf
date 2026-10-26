using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Looks mods up on their sites: one from a link to it, with its game, files and changelog; or a game's mods by search.
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
    /// One page of a game's mods that match <paramref name="query"/>, from the game's source.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The game's source isn't registered or can't search, or the source doesn't know the game.
    /// </exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused the search.</exception>
    Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken);
}
