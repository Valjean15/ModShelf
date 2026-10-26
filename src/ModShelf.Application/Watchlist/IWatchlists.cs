using ModShelf.Domain.Mods;

namespace ModShelf.Application.Watchlist;

/// <summary>
/// The mods the linked accounts follow on their sites, such as Nexus Mods' tracked mods: listed by site, and followed
/// or no longer followed by link.
/// </summary>
public interface IWatchlists
{
    /// <summary>
    /// The sources with a watchlist, by ID.
    /// </summary>
    IReadOnlyList<SourceId> Sources { get; }

    /// <returns>Every mod the account linked to <paramref name="source"/> follows, of every game, in no particular order.</returns>
    /// <exception cref="ArgumentException">The source isn't registered, or has no watchlist.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModRef>> GetWatchedAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// Follows the mod the link is to, or to one of whose files. Following one already followed changes nothing.
    /// </summary>
    /// <returns>The mod now followed.</returns>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link, or its site has no watchlist.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    Task<ModRef> WatchAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Stops following the mod the link is to. Unfollowing one not followed changes nothing.
    /// </summary>
    /// <returns>The mod no longer followed.</returns>
    /// <inheritdoc cref="WatchAsync" path="/exception"/>
    Task<ModRef> UnwatchAsync(Uri link, CancellationToken cancellationToken);
}
