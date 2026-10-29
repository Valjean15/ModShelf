using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Links, lists and unlinks accounts on the sources that support them, one account per source.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Every source that can link an account, with the account when one is linked.
    /// </summary>
    Task<IReadOnlyList<SourceAccountStatus>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Links an account on <paramref name="source"/>, replacing any account linked before. For an API key or
    /// an emailed code it asks through <paramref name="prompt"/>; for OAuth it opens the browser and waits
    /// for the user to approve.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No registered source with that ID can link accounts.</exception>
    /// <exception cref="ArgumentException">
    /// The source doesn't offer <paramref name="kind"/>, or the user's answer can't be used, such as a blank API key or
    /// an email address without an @. The message says which, in words the user can act on.
    /// </exception>
    /// <exception cref="AuthenticationFailedException">The source refused what the user entered.</exception>
    Task<LinkedAccount> LinkAsync(SourceId source, AuthKind kind, IAuthPrompt prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the site who the linked account is now, with its stored credentials, and keeps what it says, such as a tier
    /// the account has moved to. The site's answer also brings its quota up to date, where it has one.
    /// </summary>
    /// <returns>The account as it's linked now.</returns>
    /// <exception cref="KeyNotFoundException">No registered source with that ID can link accounts, or no account is linked on it.</exception>
    /// <exception cref="AuthenticationFailedException">
    /// The site refused the account's credentials, or they can't be used any more; it has to be linked again.
    /// </exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or answered with an error.</exception>
    Task<LinkedAccount> CheckAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the source's account and its credentials. Does nothing when none is linked.
    /// </summary>
    Task UnlinkAsync(SourceId source, CancellationToken cancellationToken);
}
