using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Watchlist;

/// <summary>
/// The mods the linked account follows on the site, such as GameBanana's subscriptions, to read: a site whose list
/// ModShelf can change too is an <see cref="IWatchlistSync"/>.
/// </summary>
/// <remarks>
/// It reads the linked account's list, so it needs one. The site names what it follows in its
/// <see cref="IModSource.Words"/>' <see cref="Common.SiteWords.Followed"/>.
/// </remarks>
public interface IWatchlistReader : IModSource
{
    /// <returns>Every mod the account follows, of every game, in no particular order.</returns>
    /// <exception cref="Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken);
}
