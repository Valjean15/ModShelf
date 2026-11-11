using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.ModIo.Accounts;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Tests.Common;

namespace ModShelf.Sources.ModIo.Tests.Accounts;

/// <summary>
/// One box for an API key or an access token, told apart by their shape, each sent as mod.io reads it.
/// </summary>
public sealed class ModIoAccessStrategyTests
{
    private static readonly AuthChallenge Challenge = new(AuthKind.ApiKey, ModIoAccessStrategy.AccessPage);

    private readonly ModIoAccessStrategy _strategy = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RecordedModIoApi.ApiKey)]
    [InlineData("  0123456789ABCDEF0123456789ABCDEF\n")]
    public async Task TakesAnApiKey(string pasted)
    {
        AccountCredentials credentials = await _strategy.CompleteAsync(Challenge, pasted, Cancellation);

        Assert.Equal(pasted.Trim(), credentials.AccessToken);
        Assert.Equal(ModIoCredential.ApiKey, ModIoAccessStrategy.Of(credentials));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Bearer ")]
    [InlineData("bearer ")]
    [InlineData("Bearer \t ")]
    public async Task TakesATokenWithOrWithoutBearerBeforeIt(string before)
    {
        AccountCredentials credentials = await _strategy.CompleteAsync(Challenge, before + RecordedModIoApi.AccessToken + " ", Cancellation);

        Assert.Equal(RecordedModIoApi.AccessToken, credentials.AccessToken);
        Assert.Equal(ModIoCredential.AccessToken, ModIoAccessStrategy.Of(credentials));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("my password")]
    [InlineData("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJhdWQiOiIxIiwianRpIjoiYSJ9 signature")]
    [InlineData("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9\"<script>")]
    [InlineData("   ")]
    public async Task RefusesWhatIsNeitherByItsShapeWithoutQuotingIt(string pasted)
    {
        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(() => _strategy.CompleteAsync(Challenge, pasted, Cancellation));

        Assert.Equal(
            "That's neither a mod.io API key, 32 letters and digits, nor an access token, which is far longer. Copy one from the API Access page of your mod.io account.",
            refused.Message);
    }

    [Theory]
    [InlineData("https://g-6715.modapi.io/v1/games/6715/mods?_limit=1", "https://g-6715.modapi.io/v1/games/6715/mods?_limit=1&api_key=0123456789abcdef0123456789abcdef")]
    [InlineData("https://api.mod.io/v1/me", "https://api.mod.io/v1/me?api_key=0123456789abcdef0123456789abcdef")]
    [InlineData("https://api.mod.io/v1/games?api_key=old&_q=a%20b", "https://api.mod.io/v1/games?_q=a%20b&api_key=0123456789abcdef0123456789abcdef")]
    [InlineData("https://api.mod.io/v1/games?API_KEY=old", "https://api.mod.io/v1/games?api_key=0123456789abcdef0123456789abcdef")]
    public void SendsAnApiKeyAsTheQuerysApiKeyInPlaceOfAnyBefore(string address, string sent)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, address);
        request.Headers.Authorization = new("Bearer", "old");

        _strategy.Authorize(request, new AccountCredentials(RecordedModIoApi.ApiKey));

        Assert.Equal(sent, request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        Assert.True(request.Options.TryGetValue(ModIoRequests.Sent, out ModIoCredential credential) && credential == ModIoCredential.ApiKey);
    }

    [Fact]
    public void SendsAnApiKeyOnARelativeAddressToo()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("games?_limit=1", UriKind.Relative));

        _strategy.Authorize(request, new AccountCredentials(RecordedModIoApi.ApiKey));

        Assert.Equal($"games?_limit=1&api_key={RecordedModIoApi.ApiKey}", request.RequestUri!.OriginalString);
    }

    [Theory]
    [InlineData("https://api.mod.io/v1/me?api_key=old", "https://api.mod.io/v1/me")]
    [InlineData("https://api.mod.io/v1/games?_limit=1&api_key=old", "https://api.mod.io/v1/games?_limit=1")]
    public void SendsATokenAsTheBearerTokenInPlaceOfAKey(string address, string sent)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, address);

        _strategy.Authorize(request, new AccountCredentials(RecordedModIoApi.AccessToken));

        Assert.Equal(sent, request.RequestUri!.AbsoluteUri);
        Assert.Equal(("Bearer", RecordedModIoApi.AccessToken), (request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));
        Assert.True(request.Options.TryGetValue(ModIoRequests.Sent, out ModIoCredential credential) && credential == ModIoCredential.AccessToken);
    }

    [Fact]
    public void ARequestWithoutAnAddressIsLeftWithout()
    {
        using HttpRequestMessage request = new();

        _strategy.Authorize(request, new AccountCredentials(RecordedModIoApi.ApiKey));

        Assert.Null(request.RequestUri);
    }
}
