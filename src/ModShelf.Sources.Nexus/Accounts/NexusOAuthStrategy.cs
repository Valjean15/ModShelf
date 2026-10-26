using System.Buffers.Text;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Api;

namespace ModShelf.Sources.Nexus.Accounts;

/// <summary>
/// Links an account through the browser: OAuth 2.0 with PKCE (S256) against Nexus Mods' authorization server, with
/// the app's loopback address as the redirect. The access token goes to the API as a bearer token, and is refreshed
/// with the refresh token.
/// </summary>
/// <remarks>
/// Offered only once Nexus Mods has issued ModShelf a client ID. The authorization and token requests go through the
/// source's client too, so they carry the app's headers and retries; the app leaves the credentials on them alone
/// while it links or refreshes.
/// </remarks>
internal sealed class NexusOAuthStrategy(NexusApiClient api, TimeProvider timeProvider, string clientId, Uri usersBaseAddress) : IAuthStrategy
{
    /// <summary>
    /// <c>public</c> for the API, <c>openid</c> for who the user is.
    /// </summary>
    public const string Scopes = "openid public";

    private const string VerifierKey = "code_verifier";
    private const string StateKey = "state";
    private const string RedirectUriKey = "redirect_uri";

    public AuthKind Kind => AuthKind.OAuthPkce;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        Uri redirectUri = start.RedirectUri ?? throw new ArgumentException("Signing in through the browser needs the loopback redirect address.", nameof(start));

        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        string query = string.Join('&', new (string Name, string Value)[]
        {
            ("response_type", "code"),
            ("client_id", clientId),
            ("redirect_uri", redirectUri.AbsoluteUri),
            ("scope", Scopes),
            ("code_challenge", Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
            ("code_challenge_method", "S256"),
            ("state", state),
        }.Select(pair => $"{pair.Name}={Uri.EscapeDataString(pair.Value)}"));

        return Task.FromResult(new AuthChallenge(
            AuthKind.OAuthPkce,
            new Uri(usersBaseAddress, $"oauth/authorize?{query}"),
            state: new Dictionary<string, string>
            {
                [VerifierKey] = verifier,
                [StateKey] = state,
                [RedirectUriKey] = redirectUri.AbsoluteUri,
            }));
    }

    /// <param name="answer">The full redirect URI the browser came back to.</param>
    /// <exception cref="AuthenticationFailedException">
    /// The user turned ModShelf down, the browser came back from another sign-in, or Nexus Mods refused the code.
    /// </exception>
    public async Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        NameValueCollection returned = HttpUtility.ParseQueryString(new Uri(answer).Query);
        if (returned["state"] != challenge.State[StateKey])
        {
            throw new AuthenticationFailedException("The browser came back from another sign-in. Try again.");
        }

        if (returned["error"] is { } error)
        {
            throw new AuthenticationFailedException($"Nexus Mods didn't let ModShelf in ({error}).");
        }

        if (returned["code"] is not { Length: > 0 } code)
        {
            throw new AuthenticationFailedException("Nexus Mods came back without a sign-in code.");
        }

        NexusTokens tokens = await RequestTokensAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = challenge.State[RedirectUriKey],
                    ["code_verifier"] = challenge.State[VerifierKey],
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Credentials(tokens, previousRefreshToken: null);
    }

    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        request.Headers.Remove(NexusApiKeyStrategy.HeaderName);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
    }

    /// <returns>Fresh credentials, or null when Nexus Mods refuses the refresh token and the user has to sign in again.</returns>
    public async Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expired);
        if (expired.RefreshToken is null)
        {
            return null;
        }

        try
        {
            NexusTokens tokens = await RequestTokensAsync(
                    new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = expired.RefreshToken },
                    cancellationToken)
                .ConfigureAwait(false);
            return Credentials(tokens, expired.RefreshToken);
        }
        catch (AuthenticationFailedException)
        {
            return null;
        }
    }

    /// <exception cref="AuthenticationFailedException">The token endpoint refused the grant (400 or 401).</exception>
    private async Task<NexusTokens> RequestTokensAsync(Dictionary<string, string> grant, CancellationToken cancellationToken)
    {
        grant["client_id"] = clientId;
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(usersBaseAddress, "oauth/token"))
        {
            Content = new FormUrlEncodedContent(grant),
        };

        try
        {
            return await api.SendAsync<NexusTokens>(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            // OAuth answers a code or refresh token it won't take with 400 and "invalid_grant".
            throw new AuthenticationFailedException("Nexus Mods refused the sign-in.", exception);
        }
    }

    /// <param name="previousRefreshToken">Kept when the answer has no new refresh token.</param>
    /// <exception cref="HttpRequestException">The answer has no access token.</exception>
    private AccountCredentials Credentials(NexusTokens tokens, string? previousRefreshToken)
        => string.IsNullOrWhiteSpace(tokens.AccessToken)
            ? throw new HttpRequestException("Nexus Mods answered the sign-in without an access token.")
            : new AccountCredentials(
                tokens.AccessToken,
                tokens.RefreshToken ?? previousRefreshToken,
                tokens.ExpiresIn is { } seconds ? timeProvider.GetUtcNow().AddSeconds(seconds) : null);
}
