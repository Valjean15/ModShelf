using ModShelf.Application.Accounts;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Accounts;

/// <summary>
/// How <see cref="ICredentialProvider.AuthorizeAsync"/> adds a linked account to a request, and which requests it
/// leaves alone.
/// </summary>
public sealed class RequestAuthorizationTests
{
    private static readonly TimeSpan NoDeadlock = TimeSpan.FromSeconds(10);

    private readonly AccountHarness _harness = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOAuthAccountIsSentAsABearerToken()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        using HttpRequestMessage request = NewRequest();

        AccountCredentials? added = await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation);

        Assert.Equal("access-1", added?.AccessToken);
        Assert.Equal("Bearer access-1", request.Headers.Authorization?.ToString());
        Assert.False(request.Headers.Contains(FakeStrategy.ApiKeyHeader));
    }

    [Fact]
    public async Task AnApiKeyAccountIsSentTheWayItsStrategySendsKeys()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        using HttpRequestMessage request = NewRequest();

        await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation);

        Assert.Equal(["token-for-good-key"], request.Headers.GetValues(FakeStrategy.ApiKeyHeader));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task CredentialsRefreshedBeforeUseAreTheOnesSent()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));
        using HttpRequestMessage request = NewRequest();

        await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation);

        Assert.Equal("Bearer access-2", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task WithoutAnAccountTheRequestIsLeftAlone()
    {
        using HttpRequestMessage request = NewRequest();

        Assert.Null(await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation));
        Assert.Empty(request.Headers);
    }

    [Fact]
    public async Task AnAccountThatNeedsLinkingAgainIsNotSent()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.NexusOAuth.Refresh = _ => Task.FromResult<AccountCredentials?>(null);
        _harness.Time.Advance(TimeSpan.FromHours(2));
        using HttpRequestMessage request = NewRequest();

        Assert.Null(await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation));
        Assert.Empty(request.Headers);
    }

    [Fact]
    public async Task AnAccountLinkedInAWayTheSourceNoLongerOffersIsNotSent()
    {
        // Nexus offers OAuth and API keys, not emailed codes.
        _harness.Secrets.Secrets["account/nexus"] = """{"version":1,"kind":3,"accessToken":"t"}""";
        using HttpRequestMessage request = NewRequest();

        Assert.Null(await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation));
        Assert.Empty(request.Headers);
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("Can't add the account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestsSentWhileLinkingTheSourceAreLeftAlone()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        List<HttpRequestMessage> sentWhileLinking = [];
        _harness.NexusApiKey.WhileCompleting = () => SendAsync(TestData.Nexus, sentWhileLinking);
        _harness.Nexus.WhileLookingUpProfile = () => SendAsync(TestData.Nexus, sentWhileLinking);

        // Linking again: the old OAuth token must not ride along while the new key is checked.
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey).WaitAsync(NoDeadlock, Cancellation);

        Assert.Equal(2, sentWhileLinking.Count);
        Assert.All(sentWhileLinking, request => Assert.Empty(request.Headers));
    }

    [Fact]
    public async Task RequestsSentWhileRefreshingTheSourceAreLeftAloneWithoutWaiting()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync(TestData.Nexus))!;
        List<HttpRequestMessage> sentWhileRefreshing = [];
        _harness.NexusOAuth.Refresh = async _ =>
        {
            await SendAsync(TestData.Nexus, sentWhileRefreshing);
            return _harness.NexusOAuth.NewTokens();
        };

        AccountCredentials? refreshed = await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation)
            .WaitAsync(NoDeadlock, Cancellation);

        Assert.Equal("access-2", refreshed?.AccessToken);
        Assert.Empty(Assert.Single(sentWhileRefreshing).Headers);
    }

    [Fact]
    public async Task RequestsSentWhileRefreshingBeforeUseAreLeftAloneWithoutWaiting()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));
        List<HttpRequestMessage> sentWhileRefreshing = [];
        _harness.NexusOAuth.Refresh = async _ =>
        {
            await SendAsync(TestData.Nexus, sentWhileRefreshing);
            return _harness.NexusOAuth.NewTokens();
        };

        AccountCredentials? added = await AuthorizeAsync(TestData.Nexus).WaitAsync(NoDeadlock, Cancellation);

        Assert.Equal("access-2", added?.AccessToken);
        Assert.Empty(Assert.Single(sentWhileRefreshing).Headers);
    }

    [Fact]
    public async Task OtherSourcesKeepTheirAccountsWhileOneIsRefreshed()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        await _harness.LinkAsync(TestData.ModIo, AuthKind.EmailCode);
        AccountCredentials refused = (await AuthorizeAsync(TestData.Nexus))!;
        List<HttpRequestMessage> sentToModIo = [];
        _harness.NexusOAuth.Refresh = async _ =>
        {
            await SendAsync(TestData.ModIo, sentToModIo);
            return _harness.NexusOAuth.NewTokens();
        };

        await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation).WaitAsync(NoDeadlock, Cancellation);

        Assert.Equal("Bearer token-for-123456", Assert.Single(sentToModIo).Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task OnceLinkingIsDoneRequestsGetTheNewAccount()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        Assert.Equal("token-for-good-key", (await AuthorizeAsync(TestData.Nexus))?.AccessToken);
    }

    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, "https://api.example.com/v1/users/validate.json");

    /// <summary>
    /// What a source's API client does with a request: asks the provider to add the account, and keeps the request.
    /// </summary>
    private async Task SendAsync(SourceId source, List<HttpRequestMessage> sent)
    {
        HttpRequestMessage request = NewRequest();
        sent.Add(request);
        await _harness.Service.AuthorizeAsync(source, request, Cancellation);
    }

    private async Task<AccountCredentials?> AuthorizeAsync(SourceId source)
    {
        using HttpRequestMessage request = NewRequest();
        return await _harness.Service.AuthorizeAsync(source, request, Cancellation);
    }
}
