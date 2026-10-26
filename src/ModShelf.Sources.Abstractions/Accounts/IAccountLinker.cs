namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// A source whose users can link an account, for downloads, higher limits or features that need one.
/// </summary>
public interface IAccountLinker : IModSource
{
    /// <summary>
    /// Whether the source can be used at all without a linked account.
    /// </summary>
    bool WorksWithoutAccount { get; }

    /// <summary>
    /// The ways to link an account, in the order to offer them. At least one, and at most one per kind.
    /// </summary>
    IReadOnlyList<IAuthStrategy> Strategies { get; }

    /// <summary>
    /// Looks up the account the credentials belong to.
    /// </summary>
    /// <param name="kind">
    /// The way the credentials were obtained, one of <see cref="Strategies"/>. It decides how they're sent, such as an
    /// API key header or a bearer token.
    /// </param>
    /// <exception cref="AuthenticationFailedException">The site refused the credentials.</exception>
    Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken);
}
