using ModShelf.Application.Accounts;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Accounts;

/// <summary>
/// The <see cref="ICredentialProvider"/> side: which credentials requests get, and refreshing them.
/// </summary>
public sealed class CredentialRefreshTests
{
    private readonly AccountHarness _harness = new();

    [Fact]
    public async Task HasNoCredentialsForASourceWithoutAnAccount()
    {
        Assert.Null(await AuthorizeAsync());
    }

    [Fact]
    public async Task CredentialsSurviveARestart()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);

        _harness.Restart();

        Assert.Equal("access-1", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal(0, _harness.NexusOAuth.RefreshCalls);
    }

    [Fact]
    public async Task CredentialsThatStillWorkAreNotRefreshed()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(58));

        Assert.Equal("access-1", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal(0, _harness.NexusOAuth.RefreshCalls);
    }

    [Fact]
    public async Task CredentialsAboutToExpireAreRefreshedBeforeUseAndKept()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));

        AccountCredentials? credentials = await AuthorizeAsync();

        Assert.Equal("access-2", credentials?.AccessToken);
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
        _harness.Restart();
        Assert.Equal("access-2", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task AnApiKeyWithoutExpiryIsNeverRefreshedBeforeUse()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Time.Advance(TimeSpan.FromDays(400));

        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal(0, _harness.NexusApiKey.RefreshCalls);
    }

    [Fact]
    public async Task RefusedCredentialsAreRefreshed()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;

        AccountCredentials? refreshed = await RefreshAsync(refused);

        Assert.Equal("access-2", refreshed?.AccessToken);
        Assert.Equal("access-2", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task CredentialsRefusedAfterAnotherRequestRefreshedThemAreNotRefreshedAgain()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        await RefreshAsync(refused);

        AccountCredentials? second = await RefreshAsync(refused);

        Assert.Equal("access-2", second?.AccessToken);
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
    }

    [Fact]
    public async Task RequestsRefusedAtTheSameMomentShareOneRefresh()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        TaskCompletionSource<AccountCredentials?> siteAnswers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.NexusOAuth.Refresh = _ => siteAnswers.Task;

        Task<AccountCredentials?>[] requests = [.. Enumerable.Range(0, 5).Select(_ => RefreshAsync(refused))];
        siteAnswers.SetResult(_harness.NexusOAuth.NewTokens());
        AccountCredentials?[] results = await Task.WhenAll(requests);

        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
        Assert.All(results, result => Assert.Equal("access-2", result?.AccessToken));
    }

    [Fact]
    public async Task CredentialsThatCannotBeRefreshedNeedTheAccountLinkedAgain()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.NexusOAuth.Refresh = _ => Task.FromResult<AccountCredentials?>(null);

        Assert.Null(await RefreshAsync(refused));
        Assert.Null(await AuthorizeAsync());
        Assert.Null(await RefreshAsync(refused));
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);

        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task ARefreshTheSiteRefusesNeedsTheAccountLinkedAgain()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.NexusOAuth.Refresh = _ => throw new AuthenticationFailedException("The refresh token was revoked.");

        Assert.Null(await RefreshAsync(refused));
        Assert.Null(await AuthorizeAsync());
    }

    [Fact]
    public async Task ANetworkErrorBeforeExpiryKeepsTheCurrentCredentials()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));
        _harness.NexusOAuth.Refresh = _ => throw new HttpRequestException("The site didn't answer.");

        Assert.Equal("access-1", (await AuthorizeAsync())?.AccessToken);
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("Couldn't refresh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANetworkErrorAfterARefusalReachesTheCaller()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.NexusOAuth.Refresh = _ => throw new HttpRequestException("The site didn't answer.");

        await Assert.ThrowsAsync<HttpRequestException>(() => RefreshAsync(refused));
        Assert.Equal("access-1", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task RefreshedCredentialsThatCantBeSavedAreKeptWhileTheAppRuns()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.Secrets.WriteFailure = new InvalidOperationException("The Secret Service couldn't store a secret: Timeout was reached");

        AccountCredentials? refreshed = await RefreshAsync(refused);

        Assert.Equal("access-2", refreshed?.AccessToken);
        Assert.Equal("access-2", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Couldn't save the refreshed credentials for nexus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CredentialsRefreshedBeforeUseThatCantBeSavedAreKeptWhileTheAppRuns()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));
        _harness.Secrets.WriteFailure = new InvalidOperationException("The Secret Service couldn't store a secret: Timeout was reached");

        Assert.Equal("access-2", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal("access-2", (await AuthorizeAsync())?.AccessToken);
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
    }

    [Fact]
    public async Task RefreshedCredentialsAreSavedOnceTheyCanBe()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.Secrets.WriteFailure = new InvalidOperationException("The Secret Service couldn't store a secret: Timeout was reached");
        AccountCredentials refreshed = (await RefreshAsync(refused))!;
        _harness.Secrets.WriteFailure = null;

        await RefreshAsync(refreshed);
        _harness.Restart();

        Assert.Equal("access-3", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task DamagedCredentialsCountAsNone()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Secrets.Damaged.Add("account/nexus");

        _harness.Restart();

        Assert.Null(await AuthorizeAsync());
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("can't be read", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":99,"kind":1,"accessToken":"t"}""")]
    [InlineData("""{"version":1,"kind":99,"accessToken":"t"}""")]
    [InlineData("""{"version":1,"kind":1,"accessToken":" "}""")]
    public async Task CredentialsStoredInAnUnknownFormatCountAsNone(string stored)
    {
        _harness.Secrets.Secrets["account/nexus"] = stored;

        Assert.Null(await AuthorizeAsync());
    }

    /// <summary>
    /// The credentials the service adds to a request to Nexus.
    /// </summary>
    private async Task<AccountCredentials?> AuthorizeAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/users/validate.json");
        return await _harness.Service.AuthorizeAsync(TestData.Nexus, request, TestContext.Current.CancellationToken);
    }

    private Task<AccountCredentials?> RefreshAsync(AccountCredentials refused)
        => _harness.Service.RefreshAsync(TestData.Nexus, refused, TestContext.Current.CancellationToken);
}
