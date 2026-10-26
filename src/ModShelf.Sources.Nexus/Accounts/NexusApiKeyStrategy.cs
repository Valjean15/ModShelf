using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.Nexus.Accounts;

/// <summary>
/// Links an account with the user's personal API key, which the API takes in an <c>apikey</c> header.
/// </summary>
/// <remarks>
/// The key is checked when the source looks up whose it is. A key can't be refreshed: one the API refuses has to be
/// linked again.
/// </remarks>
internal sealed class NexusApiKeyStrategy : IAuthStrategy
{
    /// <summary>
    /// The header the API reads the key from.
    /// </summary>
    public const string HeaderName = "apikey";

    /// <summary>
    /// Where a signed-in user finds their personal API key.
    /// </summary>
    public static readonly Uri KeyPage = new("https://next.nexusmods.com/settings/api-keys");

    public AuthKind Kind => AuthKind.ApiKey;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
        => Task.FromResult(new AuthChallenge(AuthKind.ApiKey, KeyPage));

    public Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
        => Task.FromResult(new AccountCredentials(answer));

    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, credentials.AccessToken);
    }

    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
