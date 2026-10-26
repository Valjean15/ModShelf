using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Watchlist;

/// <summary>
/// The mods the linked account follows on the site, such as Nexus Mods' tracked mods, to read and to change.
/// </summary>
/// <remarks>
/// Every method acts on the linked account, so each needs one.
/// </remarks>
public interface IWatchlistSync : IModSource
{
    /// <returns>Every mod the account follows, of every game, in no particular order.</returns>
    /// <exception cref="Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Follows the mod. Following one already followed changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't this source's.</exception>
    /// <exception cref="Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    Task WatchAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Stops following the mod. Unfollowing one not followed changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't this source's.</exception>
    /// <exception cref="Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken);
}
