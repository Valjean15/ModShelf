using System.Text.Json;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Accounts;

public sealed class AccountLinkingTests
{
    private readonly AccountHarness _harness = new();

    [Fact]
    public async Task ListsEverySourceThatCanLinkAnAccount()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        IReadOnlyList<SourceAccountStatus> statuses = await _harness.Service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([TestData.Nexus, TestData.ModIo], statuses.Select(status => status.Source));
        SourceAccountStatus nexus = statuses[0];
        Assert.Equal("nexus site", nexus.DisplayName);
        Assert.False(nexus.WorksWithoutAccount);
        Assert.Equal([AuthKind.OAuthPkce, AuthKind.ApiKey], nexus.AuthKinds);
        Assert.Equal("Jean", nexus.Account?.Profile.DisplayName);
        Assert.True(statuses[1].WorksWithoutAccount);
        Assert.Null(statuses[1].Account);
    }

    [Fact]
    public async Task LinksWithAnApiKey()
    {
        LinkedAccount account = await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        Assert.Equal(["api key"], _harness.Prompt.Asked);
        Assert.Equal(new Uri("https://example.com/account/api-keys"), _harness.Prompt.KeyPage);
        Assert.Equal(TestData.Nexus, account.Source);
        Assert.Equal(AuthKind.ApiKey, account.Kind);
        Assert.Equal("Premium", account.Profile.Tier);
        Assert.Equal(TestData.Now, account.LinkedAt);
        Assert.Same(account, _harness.Accounts.Accounts[TestData.Nexus]);
        Assert.Equal("token-for-good-key", (await CredentialsAsync(TestData.Nexus))?.AccessToken);
    }

    [Theory]
    [InlineData(AuthKind.ApiKey)]
    [InlineData(AuthKind.OAuthPkce)]
    public async Task LooksUpTheProfileWithTheWayItWasLinked(AuthKind kind)
    {
        // Nexus offers both ways, and sends the credentials of each differently.
        await _harness.LinkAsync(TestData.Nexus, kind);

        Assert.Equal([kind], _harness.Nexus.ProfileKinds);
    }

    [Fact]
    public async Task TrimsWhatTheUserPasted()
    {
        _harness.Prompt.ApiKey = $"  {Fakes.FakeStrategy.GoodApiKey}\n";

        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        Assert.Equal([Fakes.FakeStrategy.GoodApiKey], _harness.NexusApiKey.Answers);
    }

    [Fact]
    public async Task LinksWithAnEmailedCode()
    {
        LinkedAccount account = await _harness.LinkAsync(TestData.ModIo, AuthKind.EmailCode);

        Assert.Equal(["email", "email code"], _harness.Prompt.Asked);
        Assert.Equal("jean@example.com", Assert.Single(_harness.ModIoEmail.Starts).Email);
        Assert.Equal("j***@example.com", _harness.Prompt.SentTo);
        Assert.Equal(AuthKind.EmailCode, account.Kind);
    }

    [Fact]
    public async Task LinksInTheBrowser()
    {
        LinkedAccount account = await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);

        Uri opened = Assert.Single(_harness.Browser.Opened);
        Assert.Equal("auth.example.com", opened.Host);
        Assert.Equal(new Uri("http://127.0.0.1:49152/"), Assert.Single(_harness.NexusOAuth.Starts).RedirectUri);
        Assert.Equal((opened, true), _harness.Prompt.BrowserStep);
        Assert.Equal(AuthKind.OAuthPkce, account.Kind);
        Assert.Equal("refresh-1", (await CredentialsAsync(TestData.Nexus))?.RefreshToken);
        Assert.True(_harness.Browser.RedirectDisposed);
    }

    [Fact]
    public async Task ShowsTheLinkWhenTheBrowserDoesNotOpen()
    {
        _harness.Browser.CanOpen = false;

        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);

        Assert.False(_harness.Prompt.BrowserStep?.Opened);
    }

    [Fact]
    public async Task CancellingWhileWaitingForTheBrowserStopsListeningAndSavesNothing()
    {
        _harness.Browser.UserApproves = false;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<LinkedAccount> linking = _harness.Service.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce, _harness.Prompt, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => linking);
        Assert.True(_harness.Browser.RedirectDisposed);
        Assert.Empty(_harness.Accounts.Accounts);
        Assert.Empty(_harness.Secrets.Secrets);
    }

    [Theory]
    [InlineData(AuthKind.ApiKey)]
    [InlineData(AuthKind.OAuthPkce)]
    public async Task ARefusedAnswerSavesNothing(AuthKind kind)
    {
        _harness.Prompt.ApiKey = "wrong-key";
        _harness.Browser.Code = "wrong-code";

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _harness.LinkAsync(TestData.Nexus, kind));

        Assert.Empty(_harness.Accounts.Accounts);
        Assert.Empty(_harness.Secrets.Secrets);
    }

    [Fact]
    public async Task AProfileTheSiteRefusesSavesNothing()
    {
        _harness.Nexus.RefusesProfiles = true;

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey));

        Assert.Empty(_harness.Accounts.Accounts);
        Assert.Empty(_harness.Secrets.Secrets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsABlankAnswer(string answer)
    {
        _harness.Prompt.ApiKey = answer;

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey));

        Assert.Empty(_harness.NexusApiKey.Answers);
    }

    [Fact]
    public async Task LinkingAgainReplacesTheAccount()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Time.Advance(TimeSpan.FromDays(1));

        LinkedAccount again = await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);

        Assert.Same(again, Assert.Single(_harness.Accounts.Accounts).Value);
        Assert.Equal(TestData.Now.AddDays(1), again.LinkedAt);
        Assert.Equal("access-1", (await CredentialsAsync(TestData.Nexus))?.AccessToken);
    }

    [Fact]
    public async Task ALinkWhoseAccountCantBeSavedPutsBackTheCredentialsItHad()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Accounts.SaveFailure = new InvalidOperationException("The library database is locked.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce));

        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
        _harness.Restart();
        Assert.Equal("token-for-good-key", (await AuthorizeAsync())?.AccessToken);
    }

    [Fact]
    public async Task AFirstLinkWhoseAccountCantBeSavedLeavesNoCredentials()
    {
        _harness.Accounts.SaveFailure = new InvalidOperationException("The library database is locked.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey));

        Assert.Empty(_harness.Secrets.Secrets);
        Assert.Null(await AuthorizeAsync());
    }

    [Fact]
    public async Task UnlinkingRemovesTheAccountAndItsCredentials()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        await _harness.Service.UnlinkAsync(TestData.Nexus, TestContext.Current.CancellationToken);

        Assert.Empty(_harness.Accounts.Accounts);
        Assert.Empty(_harness.Secrets.Secrets);
        Assert.Null(await CredentialsAsync(TestData.Nexus));
    }

    [Fact]
    public async Task UnlinkingASourceWithoutAnAccountDoesNothing()
    {
        await _harness.Service.UnlinkAsync(TestData.ModIo, TestContext.Current.CancellationToken);

        Assert.Empty(_harness.Accounts.Accounts);
    }

    [Fact]
    public async Task RefusesSourcesThatCannotLinkAndKindsTheyDoNotOffer()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _harness.LinkAsync(new SourceId("unknown"), AuthKind.ApiKey));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _harness.LinkAsync(AccountHarness.DirectId, AuthKind.ApiKey));
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.LinkAsync(TestData.ModIo, AuthKind.OAuthPkce));
    }

    [Fact]
    public async Task KeepsCredentialsInTheSecretStoreOnly()
    {
        LinkedAccount account = await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);

        string secret = Assert.Single(_harness.Secrets.Secrets, entry => entry.Key == "account/nexus").Value;
        using JsonDocument json = JsonDocument.Parse(secret);
        Assert.Equal("access-1", json.RootElement.GetProperty("accessToken").GetString());
        Assert.DoesNotContain("access-1", account.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeverLogsCredentialsOrAnswers()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        await _harness.Service.UnlinkAsync(TestData.Nexus, TestContext.Current.CancellationToken);

        Assert.NotEmpty(_harness.Logger.Entries);
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("good-key", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("access-", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("c0de", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("Jean", StringComparison.Ordinal));
    }

    /// <summary>
    /// The credentials the service adds to a request to <paramref name="source"/>.
    /// </summary>
    private async Task<AccountCredentials?> CredentialsAsync(SourceId source)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/users/validate.json");
        return await _harness.Service.AuthorizeAsync(source, request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The credentials the service adds to a request to Nexus.
    /// </summary>
    private async Task<AccountCredentials?> AuthorizeAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/users/validate.json");
        return await _harness.Service.AuthorizeAsync(TestData.Nexus, request, TestContext.Current.CancellationToken);
    }
}
