using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// The mods the source's site says are trending for a game, as its front page for the game shows them.
/// </summary>
public interface ITrendingMods : IModSource
{
    /// <returns>
    /// The trending mods, most trending first, described as <see cref="IModCatalog.GetModAsync"/> describes them, adult
    /// ones included: the caller leaves out what the user doesn't want to see.
    /// </returns>
    /// <exception cref="ArgumentException">The game isn't this source's.</exception>
    /// <exception cref="Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModInfo>> GetTrendingAsync(GameRef game, CancellationToken cancellationToken);
}
