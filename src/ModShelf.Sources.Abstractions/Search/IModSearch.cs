using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// Finds a game's mods on the source's site, a page at a time, in the orders and by the filters its
/// <see cref="Capabilities"/> say.
/// </summary>
public interface IModSearch : IModSource
{
    /// <summary>
    /// What the search can do. ModShelf asks it nothing more, so a source needn't check a query against them; it may
    /// still refuse one that asks more.
    /// </summary>
    SearchCapabilities Capabilities { get; }

    /// <returns>One page of the mods that match, described as <see cref="Catalog.IModCatalog.GetModAsync"/> describes them.</returns>
    /// <exception cref="ArgumentException">The game isn't this source's, or the source can't search the way asked.</exception>
    /// <exception cref="Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused the search.</exception>
    Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken);
}
