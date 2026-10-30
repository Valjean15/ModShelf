using ModShelf.Application.Accounts;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Accounts;

/// <summary>
/// Another ModShelf process, such as the CLI while the app runs, changing the same account through the same stores.
/// </summary>
public sealed class AnotherProcessTests
{
    private readonly AccountHarness _harness = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAccountLinkedAgainElsewhereIsUsedOnceTheOldCredentialsAreRefused()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        AccountCredentials refused = (await AuthorizeAsync())!;
        await _harness.AnotherProcess().LinkAsync(TestData.Nexus, AuthKind.OAuthPkce, _harness.Prompt, Cancellation);

        AccountCredentials? now = await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation);

        Assert.Equal("access-1", now?.AccessToken);
        Assert.Equal(0, _harness.NexusApiKey.RefreshCalls);
    }

    [Fact]
    public async Task AnAccountUnlinkedElsewhereIsntSentOnceAMinuteHasPassed()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        await AuthorizeAsync();
        await _harness.AnotherProcess().UnlinkAsync(TestData.Nexus, Cancellation);

        AccountCredentials? soon = await AuthorizeAsync();
        _harness.Time.Advance(CredentialVault.ReadAgainAfter);

        Assert.Equal("token-for-good-key", soon?.AccessToken);
        Assert.Null(await AuthorizeAsync());
    }

    [Fact]
    public async Task AnAccountLinkedElsewhereIsSentOnceAMinuteHasPassed()
    {
        Assert.Null(await AuthorizeAsync());
        await _harness.AnotherProcess().LinkAsync(TestData.Nexus, AuthKind.ApiKey, _harness.Prompt, Cancellation);
        _harness.Time.Advance(CredentialVault.ReadAgainAfter);

        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task CredentialsThatNeedLinkingAgainStaySoUntilTheAccountIsLinkedAgainElsewhere()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.NexusOAuth.Refresh = _ => Task.FromResult<AccountCredentials?>(null);
        await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation);
        _harness.Time.Advance(CredentialVault.ReadAgainAfter);

        Assert.Null(await AuthorizeAsync());
        Assert.Null(await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation));
        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);

        await _harness.AnotherProcess().LinkAsync(TestData.Nexus, AuthKind.ApiKey, _harness.Prompt, Cancellation);
        _harness.Time.Advance(CredentialVault.ReadAgainAfter);
        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task CredentialsThatCantBeReadAgainAreTheOnesReadBefore()
    {
        // Such as while the keyring is locked.
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Time.Advance(CredentialVault.ReadAgainAfter);
        _harness.Secrets.ReadFailure = new InvalidOperationException("The Secret Service couldn't read a secret: the collection is locked");

        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Warning: Couldn't read the stored credentials for nexus again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACheckDoesntPutBackAnAccountUnlinkedMeanwhile()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Nexus.WhileLookingUpProfile = () => _harness.AnotherProcess().UnlinkAsync(TestData.Nexus, Cancellation);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _harness.Service.CheckAsync(TestData.Nexus, Cancellation));

        Assert.Empty(_harness.Accounts.Accounts);
        Assert.Empty(_harness.Secrets.Secrets);
    }

    [Fact]
    public async Task ACheckDoesntReplaceAnAccountLinkedAgainMeanwhile()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Nexus.WhileLookingUpProfile = async () =>
        {
            _harness.Nexus.WhileLookingUpProfile = null;
            await _harness.AnotherProcess().LinkAsync(TestData.Nexus, AuthKind.OAuthPkce, _harness.Prompt, Cancellation);
        };

        LinkedAccount checkedAccount = await _harness.Service.CheckAsync(TestData.Nexus, Cancellation);

        Assert.Equal(AuthKind.OAuthPkce, checkedAccount.Kind);
        Assert.Equal(AuthKind.OAuthPkce, Assert.Single(_harness.Accounts.Accounts.Values).Kind);
    }

    [Fact]
    public async Task RefreshedCredentialsArentSavedForAnAccountUnlinkedMeanwhile()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        AccountCredentials refused = (await AuthorizeAsync())!;
        _harness.NexusOAuth.Refresh = async _ =>
        {
            await _harness.AnotherProcess().UnlinkAsync(TestData.Nexus, Cancellation);
            return _harness.NexusOAuth.NewTokens();
        };

        Assert.Null(await _harness.Service.RefreshAsync(TestData.Nexus, refused, Cancellation));

        Assert.Empty(_harness.Secrets.Secrets);
        Assert.Null(await AuthorizeAsync());
    }

    private async Task<AccountCredentials?> AuthorizeAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/users/validate.json");
        return await _harness.Service.AuthorizeAsync(TestData.Nexus, request, Cancellation);
    }
}
