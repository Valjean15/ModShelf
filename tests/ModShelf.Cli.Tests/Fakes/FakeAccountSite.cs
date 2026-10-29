using System.Buffers.Text;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A mod site's account API, served on 127.0.0.1 for the fake sources: OAuth 2.0 with PKCE and refresh tokens that
/// rotate, a personal API key, and <c>/api/me</c>, which says whose account a request carries. Like a real site, it
/// can expire its access tokens early and the user can turn the approval down. It records every request.
/// </summary>
internal sealed partial class FakeAccountSite : IAsyncDisposable
{
    public const string ClientId = "modshelf-tests";
    public const string OAuthUser = "Robin Example";
    public const string OAuthTier = "Premium";
    public const string KeyUser = "Sam Example";
    public const string KeyTier = "Supporter";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _accepting;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PendingCode> _codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _accessTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refreshTokens = new(StringComparer.Ordinal);
    private readonly List<string> _secrets = [];
    private readonly List<SiteRequest> _requests = [];
    private int _tokenRefreshes;

    public FakeAccountSite()
    {
        ApiKey = NewSecret("key");
        _secrets.Add(ApiKey);
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync(_stopping.Token);
    }

    public Uri BaseAddress { get; }

    public Uri KeyPage => new(BaseAddress, "settings/api-keys");

    /// <summary>
    /// The user's personal API key. Any other key is refused.
    /// </summary>
    public string ApiKey { get; }

    /// <summary>
    /// How long the access tokens it issues from now on last, as the token responses say.
    /// </summary>
    public TimeSpan ExpiresIn { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether the user turns ModShelf down on the authorization page.
    /// </summary>
    public bool UserDeclines { get; set; }

    /// <summary>
    /// What the site answers when asked whose account a request carries, instead of saying, while set: such as asking
    /// to slow down, or an error of its own.
    /// </summary>
    public HttpStatusCode? ProfileRefusal { get; set; }

    /// <summary>
    /// How many times a refresh token was swapped for new tokens.
    /// </summary>
    public int TokenRefreshes
    {
        get
        {
            lock (_gate)
            {
                return _tokenRefreshes;
            }
        }
    }

    /// <summary>
    /// Every request so far, oldest first.
    /// </summary>
    public IReadOnlyList<SiteRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Every secret that went between the site and the app: the API key, authorization codes, PKCE code verifiers,
    /// and access and refresh tokens.
    /// </summary>
    public IReadOnlyList<string> Secrets
    {
        get
        {
            lock (_gate)
            {
                return [.. _secrets];
            }
        }
    }

    /// <summary>
    /// The site refuses the access tokens it issued so far, before the time it said they'd last, as sites do when a
    /// token is revoked. Their refresh tokens still work.
    /// </summary>
    public void ExpireAccessTokens()
    {
        lock (_gate)
        {
            _accessTokens.Clear();
        }
    }

    public IReadOnlyList<SiteRequest> RequestsTo(string path) => [.. Requests.Where(request => request.Path == path)];

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        _listener.Dispose();
        _stopping.Dispose();
    }

    private static string NewSecret(string kind)
        => $"{kind}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

    private static Response Json(HttpStatusCode status, Dictionary<string, object?> body)
        => new(status, JsonSerializer.Serialize(body), "application/json");

    private static Response OAuthError(string error) => Json(HttpStatusCode.BadRequest, new() { ["error"] = error });

    private static Uri RedirectTo(string redirectUri, string query) => new($"{redirectUri}?{query}");

    private static string Query(params (string Name, string Value)[] pairs)
        => string.Join('&', pairs.Select(pair => $"{pair.Name}={Uri.EscapeDataString(pair.Value)}"));

    /// <summary>
    /// Answers one request. Runs under the lock, so the site's state changes one request at a time.
    /// </summary>
    private Response Handle(Request request)
    {
        Response response = (request.Method, request.Path) switch
        {
            ("GET", "/oauth/authorize") => Authorize(request.Query),
            ("POST", "/oauth/token") => IssueTokens(request.Form),
            ("GET", "/api/me") => Me(request),
            ("GET", "/settings/api-keys") => new Response(HttpStatusCode.OK, "<p>Your API key is on this page.</p>", "text/html"),
            _ => new Response(HttpStatusCode.NotFound),
        };
        _requests.Add(new SiteRequest(request.Method, request.Path, request.Header("Authorization") ?? request.Header(FakeApiKeyStrategy.HeaderName), response.Status));
        return response;
    }

    private Response Authorize(NameValueCollection query)
    {
        string? redirectUri = query["redirect_uri"];
        string? state = query["state"];
        string? challenge = query["code_challenge"];
        bool valid = query["response_type"] == "code"
            && query["client_id"] == ClientId
            && query["code_challenge_method"] == "S256"
            && !string.IsNullOrEmpty(challenge)
            && !string.IsNullOrEmpty(state)
            && Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? redirect)
            && redirect.IsLoopback
            && string.IsNullOrEmpty(redirect.Query);
        if (!valid)
        {
            // A real site shows an error page and never sends the browser back.
            return new Response(HttpStatusCode.BadRequest, "<p>That authorization request is broken.</p>", "text/html");
        }

        if (UserDeclines)
        {
            return Response.Redirect(RedirectTo(redirectUri!, Query(("error", "access_denied"), ("state", state!))));
        }

        string code = NewSecret("code");
        _codes[code] = new PendingCode(challenge!, redirectUri!);
        _secrets.Add(code);
        return Response.Redirect(RedirectTo(redirectUri!, Query(("code", code), ("state", state!))));
    }

    private Response IssueTokens(NameValueCollection form)
    {
        if (form["client_id"] != ClientId)
        {
            return OAuthError("invalid_client");
        }

        switch (form["grant_type"])
        {
            case "authorization_code":
                string? verifier = form["code_verifier"];
                if (verifier is not null)
                {
                    _secrets.Add(verifier);
                }

                // A code works once, for the redirect URI it was issued to, with the verifier behind its challenge.
                if (form["code"] is not { } code || !_codes.Remove(code, out PendingCode? pending)
                    || form["redirect_uri"] != pending.RedirectUri
                    || verifier is null
                    || Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != pending.Challenge)
                {
                    return OAuthError("invalid_grant");
                }

                break;

            case "refresh_token":
                // Refresh tokens rotate: each works once.
                if (form["refresh_token"] is not { } refreshToken || !_refreshTokens.Remove(refreshToken))
                {
                    return OAuthError("invalid_grant");
                }

                _tokenRefreshes++;
                break;

            default:
                return OAuthError("unsupported_grant_type");
        }

        string accessToken = NewSecret("access");
        string newRefreshToken = NewSecret("refresh");
        _accessTokens.Add(accessToken);
        _refreshTokens.Add(newRefreshToken);
        _secrets.Add(accessToken);
        _secrets.Add(newRefreshToken);
        return Json(HttpStatusCode.OK, new()
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)ExpiresIn.TotalSeconds,
            ["refresh_token"] = newRefreshToken,
        });
    }

    private Response Me(Request request)
    {
        if (ProfileRefusal is { } refusal)
        {
            return new Response(refusal);
        }

        string? authorization = request.Header("Authorization");
        if (authorization is not null
            && authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            && _accessTokens.Contains(authorization["Bearer ".Length..]))
        {
            return Json(HttpStatusCode.OK, new() { ["name"] = OAuthUser, ["membership"] = OAuthTier });
        }

        if (request.Header(FakeApiKeyStrategy.HeaderName) == ApiKey)
        {
            return Json(HttpStatusCode.OK, new() { ["name"] = KeyUser, ["membership"] = KeyTier });
        }

        return new Response(HttpStatusCode.Unauthorized);
    }

    private sealed record PendingCode(string Challenge, string RedirectUri);

    private sealed record Request(string Method, string Path, NameValueCollection Query, Dictionary<string, string> Headers, NameValueCollection Form)
    {
        public string? Header(string name) => Headers.GetValueOrDefault(name);
    }

    private sealed record Response(HttpStatusCode Status, string Body = "", string? ContentType = null, Uri? Location = null)
    {
        public static Response Redirect(Uri location) => new(HttpStatusCode.Found, Location: location);

        public string StatusLine => string.Create(CultureInfo.InvariantCulture, $"HTTP/1.1 {(int)Status} {Status}");
    }
}
