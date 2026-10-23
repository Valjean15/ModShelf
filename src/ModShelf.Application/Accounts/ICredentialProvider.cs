using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Adds the linked account's credentials to requests to a source, refreshing them when needed. Used by the HTTP
/// handler on each source's API client.
/// </summary>
/// <remarks>Safe to call concurrently; refreshes of one source happen one at a time.</remarks>
public interface ICredentialProvider
{
    /// <summary>
    /// Adds the source's credentials to <paramref name="request"/> the way the account was linked, such as an API
    /// key header or an OAuth bearer token. Credentials about to expire are refreshed first.
    /// </summary>
    /// <returns>
    /// The credentials added, to pass to <see cref="RefreshAsync"/> if the source refuses them. Null when the request
    /// was left as it was: the source has no linked account, its credentials can no longer be refreshed, or the
    /// request belongs to linking or refreshing that source's account, whose strategy adds what it checks itself.
    /// </returns>
    Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// The source refused <paramref name="rejected"/>: refreshes them, unless another request already did.
    /// </summary>
    /// <returns>Credentials to retry with, or null when the user has to link the account again.</returns>
    Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken);
}
