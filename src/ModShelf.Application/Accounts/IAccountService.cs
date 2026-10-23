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
    /// Removes the source's account and its credentials. Does nothing when none is linked.
    /// </summary>
    Task UnlinkAsync(SourceId source, CancellationToken cancellationToken);
}
