namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// One way of linking an account on a source, such as an API key or OAuth, and of using the credentials it
/// produces.
/// </summary>
/// <remarks>
/// Linking takes two steps: <see cref="BeginAsync"/> says what the user has to do, and
/// <see cref="CompleteAsync"/> turns the user's answer into credentials. Credentials are secret: the app
/// keeps them in the operating system's secret store, never in the library database or the log.
/// </remarks>
public interface IAuthStrategy
{
    AuthKind Kind { get; }

    /// <summary>
    /// Starts linking.
    /// </summary>
    /// <param name="start">
    /// What the app knows before the user acts: the loopback redirect URI for <see cref="AuthKind.OAuthPkce"/>,
    /// the email address for <see cref="AuthKind.EmailCode"/>.
    /// </param>
    /// <returns>What the user has to do, and the state <see cref="CompleteAsync"/> needs.</returns>
    Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes linking.
    /// </summary>
    /// <param name="challenge">The challenge <see cref="BeginAsync"/> returned.</param>
    /// <param name="answer">
    /// The API key, the emailed code, or for <see cref="AuthKind.OAuthPkce"/> the full redirect URI the
    /// browser came back to.
    /// </param>
    /// <exception cref="AuthenticationFailedException">The site refused the answer.</exception>
    Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the credentials to a request to the source's API, such as an <c>Authorization</c> header.
    /// </summary>
    void Authorize(HttpRequestMessage request, AccountCredentials credentials);

    /// <summary>
    /// Gets fresh credentials after the old ones expired or were refused.
    /// </summary>
    /// <returns>The new credentials, or null when these can't be refreshed and the user has to link again.</returns>
    Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken);
}
