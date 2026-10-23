using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Hands out the linked account's credentials for requests to a source, refreshing them when needed.
/// Used by the HTTP handler that authorizes each source's requests.
/// </summary>
/// <remarks>Safe to call concurrently; refreshes of one source happen one at a time.</remarks>
public interface ICredentialProvider
{
    /// <summary>
    /// The source's current credentials, refreshed first when they're about to expire.
    /// </summary>
    /// <returns>Null when the source has no linked account, or its credentials can no longer be refreshed.</returns>
    Task<AccountCredentials?> GetAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// The source refused <paramref name="rejected"/>: refreshes them, unless another request already did.
    /// </summary>
    /// <returns>Credentials to retry with, or null when the user has to link the account again.</returns>
    Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken);
}
