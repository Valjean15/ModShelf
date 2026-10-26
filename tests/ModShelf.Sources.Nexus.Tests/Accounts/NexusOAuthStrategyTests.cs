using System.Buffers.Text;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests.Accounts;

/// <summary>
/// Signing in through the browser, with OAuth and PKCE. Nexus Mods hasn't issued ModShelf a client ID yet, so the sign-in
/// server's answers here are written by hand, following the OAuth standard and its discovery document, not recorded.
/// </summary>
public sealed class NexusOAuthStrategyTests : IDisposable
{
    private const string ClientId = "test-client";
    private const string TokenPath = "/oauth/token";
    private const string Tokens = """{"access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 21600, "token_type": "Bearer"}""";

    private static readonly Uri RedirectUri = new("http://127.0.0.1:49152/");

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusOAuthStrategyTests()
    {
        _services = _api.BuildServices(options => options with { OAuthClientId = ClientId });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    private IAuthStrategy Strategy => Source.Strategies.Single(strategy => strategy.Kind == AuthKind.OAuthPkce);

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public void IsOfferedFirstOnceThereIsAClientId()
    {
        Assert.Equal([AuthKind.OAuthPkce, AuthKind.ApiKey], Source.Strategies.Select(strategy => strategy.Kind));
    }

    [Fact]
    public async Task TheSignInPageAsksForACodeProtectedWithPkce()
    {
        AuthChallenge challenge = await BeginAsync();

        Assert.Equal(new Uri(_api.BaseAddress, "oauth/authorize").AbsoluteUri, challenge.Url!.GetLeftPart(UriPartial.Path));
        NameValueCollection query = HttpUtility.ParseQueryString(challenge.Url.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(RedirectUri.AbsoluteUri, query["redirect_uri"]);
        Assert.Equal("openid public", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Challenge(challenge.State["code_verifier"]), query["code_challenge"]);
        Assert.Equal(challenge.State["state"], query["state"]);
        Assert.InRange(challenge.State["code_verifier"].Length, 43, 128);
    }

    [Fact]
    public async Task EachSignInHasItsOwnVerifierAndState()
    {
        AuthChallenge first = await BeginAsync();
        AuthChallenge second = await BeginAsync();

        Assert.NotEqual(first.State["code_verifier"], second.State["code_verifier"]);
        Assert.NotEqual(first.State["state"], second.State["state"]);
    }

    [Fact]
    public async Task NeedsTheLoopbackRedirect()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Strategy.BeginAsync(new AuthStart(), Cancellation));
    }

    [Fact]
    public async Task ExchangesTheCodeWithItsVerifier()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(200).WithBody(Tokens));
        AuthChallenge challenge = await BeginAsync();

        AccountCredentials credentials = await Strategy.CompleteAsync(challenge, Returned(challenge, "code=the-code"), Cancellation);

        Assert.Equal(new AccountCredentials("access-1", "refresh-1", _api.Time.GetUtcNow().AddHours(6)), credentials);
        NameValueCollection form = Form(Assert.Single(_api.Requests));
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(RedirectUri.AbsoluteUri, form["redirect_uri"]);
        Assert.Equal(challenge.State["code_verifier"], form["code_verifier"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("ModShelf", Assert.Single(_api.Requests[0].RequestMessage!.Headers!["Application-Name"]));
    }

    [Theory]
    [InlineData("error=access_denied")]
    [InlineData("")]
    public async Task TurningModShelfDownOrComingBackWithoutACodeLinksNothing(string returned)
    {
        AuthChallenge challenge = await BeginAsync();

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Strategy.CompleteAsync(challenge, Returned(challenge, returned), Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task AnAnswerFromAnotherSignInIsRefused()
    {
        AuthChallenge challenge = await BeginAsync();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => Strategy.CompleteAsync(challenge, $"{RedirectUri}?code=the-code&state=someone-else", Cancellation));

        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task ACodeNexusModsRefusesIsAnAuthenticationFailure()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(400).WithBody("""{"error": "invalid_grant"}"""));
        AuthChallenge challenge = await BeginAsync();

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Strategy.CompleteAsync(challenge, Returned(challenge, "code=the-code"), Cancellation));

        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task AnAnswerWithoutAnAccessTokenIsAFailedRequest()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(200).WithBody("""{"token_type": "Bearer"}"""));
        AuthChallenge challenge = await BeginAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => Strategy.CompleteAsync(challenge, Returned(challenge, "code=the-code"), Cancellation));
    }

    [Fact]
    public void SendsTheTokenAsABearerTokenOnly()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(_api.BaseAddress, "v1/users/validate.json"));
        request.Headers.Add("apikey", "an-older-key");

        Strategy.Authorize(request, new AccountCredentials("access-1"));

        Assert.Equal("Bearer access-1", request.Headers.Authorization?.ToString());
        Assert.False(request.Headers.Contains("apikey"));
    }

    [Fact]
    public async Task RefreshesWithTheRefreshToken()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(200).WithBody(
            """{"access_token": "access-2", "refresh_token": "refresh-2", "expires_in": 3600}"""));

        AccountCredentials? refreshed = await Strategy.RefreshAsync(new AccountCredentials("access-1", "refresh-1"), Cancellation);

        Assert.Equal(new AccountCredentials("access-2", "refresh-2", _api.Time.GetUtcNow().AddHours(1)), refreshed);
        NameValueCollection form = Form(Assert.Single(_api.Requests));
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh-1", form["refresh_token"]);
        Assert.Equal(ClientId, form["client_id"]);
    }

    [Fact]
    public async Task KeepsTheRefreshTokenWhenNoNewOneComes()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(200).WithBody("""{"access_token": "access-2", "expires_in": 3600}"""));

        AccountCredentials? refreshed = await Strategy.RefreshAsync(new AccountCredentials("access-1", "refresh-1"), Cancellation);

        Assert.Equal("refresh-1", refreshed?.RefreshToken);
    }

    [Fact]
    public async Task ARefusedRefreshMeansSigningInAgain()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(400).WithBody("""{"error": "invalid_grant"}"""));

        Assert.Null(await Strategy.RefreshAsync(new AccountCredentials("access-1", "refresh-1"), Cancellation));
    }

    [Fact]
    public async Task CredentialsWithoutARefreshTokenCantBeRefreshed()
    {
        Assert.Null(await Strategy.RefreshAsync(new AccountCredentials("access-1"), Cancellation));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task ARefreshThatFailsForAPassingReasonIsReported()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(503));

        await Assert.ThrowsAsync<HttpRequestException>(() => Strategy.RefreshAsync(new AccountCredentials("access-1", "refresh-1"), Cancellation));
    }

    [Fact]
    public async Task WhoSignedInIsLookedUpWithTheBearerToken()
    {
        _api.Serve(RecordedNexusApi.Premium, "validate");

        AccountProfile profile = await Source.GetProfileAsync(new AccountCredentials("access-1", "refresh-1"), AuthKind.OAuthPkce, Cancellation);

        Assert.Equal("Premium", profile.Tier);
        IDictionary<string, WireMock.Types.WireMockList<string>> headers = Assert.Single(_api.Requests).RequestMessage!.Headers!;
        Assert.Equal("Bearer access-1", Assert.Single(headers["Authorization"]));
        Assert.False(headers.ContainsKey("apikey"));
    }

    [Fact]
    public async Task AnApiKeyIsStillSentAsAKey()
    {
        _api.Serve(RecordedNexusApi.Premium, "validate");

        await Source.GetProfileAsync(new AccountCredentials("key-from-the-user"), AuthKind.ApiKey, Cancellation);

        IDictionary<string, WireMock.Types.WireMockList<string>> headers = Assert.Single(_api.Requests).RequestMessage!.Headers!;
        Assert.Equal("key-from-the-user", Assert.Single(headers["apikey"]));
        Assert.False(headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task NeverLogsTokensCodesOrTheVerifier()
    {
        _api.AnswerPost(TokenPath, response => response.WithStatusCode(200).WithBody(Tokens));
        AuthChallenge challenge = await BeginAsync();
        await Strategy.CompleteAsync(challenge, Returned(challenge, "code=the-code"), Cancellation);
        await Strategy.RefreshAsync(new AccountCredentials("access-1", "refresh-1"), Cancellation);

        string logged = string.Join(Environment.NewLine, _api.Logs.Entries);

        Assert.NotEmpty(_api.Logs.Entries);
        foreach (string secret in new[] { "the-code", challenge.State["code_verifier"], "access-1", "refresh-1" })
        {
            Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
        }
    }

    private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>
    /// The address the browser comes back to, with the sign-in's state.
    /// </summary>
    private static string Returned(AuthChallenge challenge, string parameters)
        => $"{RedirectUri}?{parameters}{(parameters.Length > 0 ? "&" : string.Empty)}state={Uri.EscapeDataString(challenge.State["state"])}";

    private static NameValueCollection Form(WireMock.Logging.ILogEntry request) => HttpUtility.ParseQueryString(request.RequestMessage!.Body!);

    private Task<AuthChallenge> BeginAsync() => Strategy.BeginAsync(new AuthStart(redirectUri: RedirectUri), Cancellation);
}
