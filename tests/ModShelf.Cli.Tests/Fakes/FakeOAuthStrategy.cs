using System.Buffers.Text;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// OAuth 2.0 with PKCE against the <see cref="FakeAccountSite"/>, as a real source does it: the authorization page with
/// a code challenge and state, the code exchanged with its verifier, and refresh tokens. Every request goes through the
/// source's client.
/// </summary>
internal sealed class FakeOAuthStrategy(FakeSiteApi api, TimeProvider timeProvider) : IAuthStrategy
{
    private const string VerifierKey = "code_verifier";
    private const string StateKey = "state";
    private const string RedirectUriKey = "redirect_uri";

    public AuthKind Kind => AuthKind.OAuthPkce;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        Uri redirectUri = start.RedirectUri ?? throw new ArgumentException("OAuth needs the loopback redirect URI.", nameof(start));

        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string query = string.Join('&', new[]
        {
            ("response_type", "code"),
            ("client_id", FakeAccountSite.ClientId),
            ("redirect_uri", redirectUri.AbsoluteUri),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("state", state),
        }.Select(pair => $"{pair.Item1}={Uri.EscapeDataString(pair.Item2)}"));

        return Task.FromResult(new AuthChallenge(
            AuthKind.OAuthPkce,
            new Uri(api.Site.BaseAddress, $"oauth/authorize?{query}"),
            state: new Dictionary<string, string>
            {
                [VerifierKey] = verifier,
                [StateKey] = state,
                [RedirectUriKey] = redirectUri.AbsoluteUri,
            }));
    }

    public async Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        NameValueCollection returned = HttpUtility.ParseQueryString(new Uri(answer).Query);
        if (returned["state"] != challenge.State[StateKey])
        {
            throw new AuthenticationFailedException("The browser came back from another sign-in.");
        }

        if (returned["code"] is not { Length: > 0 } code)
        {
            throw new AuthenticationFailedException("The site didn't approve ModShelf.");
        }

        return await ExchangeAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = challenge.State[RedirectUriKey],
                    ["code_verifier"] = challenge.State[VerifierKey],
                },
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("The site refused the authorization code.");
    }

    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        request.Headers.Authorization = new("Bearer", credentials.AccessToken);
    }

    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expired);

        return expired.RefreshToken is null
            ? Task.FromResult<AccountCredentials?>(null)
            : ExchangeAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = expired.RefreshToken }, cancellationToken);
    }

    /// <returns>The tokens, or null when the site refused the grant.</returns>
    private async Task<AccountCredentials?> ExchangeAsync(Dictionary<string, string> grant, CancellationToken cancellationToken)
    {
        grant["client_id"] = FakeAccountSite.ClientId;
        using FormUrlEncodedContent form = new(grant);
        using HttpResponseMessage response = await api.CreateClient()
            .PostAsync(new Uri(api.Site.BaseAddress, "oauth/token"), form, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using JsonDocument tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        JsonElement root = tokens.RootElement;
        return new AccountCredentials(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString(),
            timeProvider.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }
}
