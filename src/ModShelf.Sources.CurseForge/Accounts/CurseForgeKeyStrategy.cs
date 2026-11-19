using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.CurseForge.Accounts;

/// <summary>
/// Links the user's own API key, which they make on the CurseForge for Studios console: CurseForge's terms let no one
/// share a key, so each user links theirs (ADR 0107). It's sent as <c>x-api-key</c> on each request.
/// </summary>
/// <remarks>
/// CurseForge checks it when the source asks for a game. It can't be refused and refreshed: one CurseForge refuses has
/// to be linked again. It's secret, so it's never logged, and messages never quote it.
/// </remarks>
internal sealed class CurseForgeKeyStrategy : IAuthStrategy
{
    /// <summary>
    /// The header CurseForge reads the key from.
    /// </summary>
    public const string KeyHeader = "x-api-key";

    // The shortest and the longest a key is taken to be: CurseForge's are 60 characters, such as $2a$10$…, or longer.
    private const int ShortestKey = 30;
    private const int LongestKey = 200;

    /// <summary>
    /// Where a signed-in user makes their API key.
    /// </summary>
    public static readonly Uri KeysPage = new("https://console.curseforge.com/#/api-keys");

    public AuthKind Kind => AuthKind.ApiKey;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
        => Task.FromResult(new AuthChallenge(AuthKind.ApiKey, KeysPage));

    /// <summary>
    /// The key pasted, without the spaces around it.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">It isn't a key, by its shape.</exception>
    public Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        string pasted = answer.Trim();
        return pasted.Length is >= ShortestKey and <= LongestKey && !pasted.Any(char.IsWhiteSpace) && !pasted.Any(char.IsControl)
            ? Task.FromResult(new AccountCredentials(pasted))
            : throw new AuthenticationFailedException(
                "That isn't a CurseForge API key. Make one on the API keys page of the CurseForge for Studios console, and copy it whole.");
    }

    /// <summary>
    /// Adds the key as the request's <c>x-api-key</c>, in place of one added before.
    /// </summary>
    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        request.Headers.Remove(KeyHeader);
        request.Headers.TryAddWithoutValidation(KeyHeader, credentials.AccessToken);
    }

    /// <summary>
    /// None: a key CurseForge refuses has to be linked again.
    /// </summary>
    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
