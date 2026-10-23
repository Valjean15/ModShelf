using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A personal API key, pasted by the user and sent in a header of the site's own.
/// </summary>
internal sealed class FakeApiKeyStrategy(Uri keyPage) : IAuthStrategy
{
    public const string HeaderName = "X-Api-Key";

    public AuthKind Kind => AuthKind.ApiKey;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
        => Task.FromResult(new AuthChallenge(AuthKind.ApiKey, keyPage));

    /// <summary>
    /// Takes the key as it is; the site checks it when the app looks up whose it is.
    /// </summary>
    public Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
        => Task.FromResult(new AccountCredentials(answer));

    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        request.Headers.Add(HeaderName, credentials.AccessToken);
    }

    /// <summary>
    /// A key can't be refreshed: one the site refuses has to be linked again.
    /// </summary>
    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
