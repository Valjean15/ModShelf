using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Application.Tests.Accounts;

/// <summary>
/// Checking a linked account with the site, and the quota the accounts are listed with.
/// </summary>
public sealed class AccountCheckTests
{
    private static readonly ApiQuota Quota = new([new QuotaAllowance(TimeSpan.FromDays(1), 100, 42, TestData.Now.AddHours(5))]);

    private readonly AccountHarness _harness = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task KeepsWhatTheSiteSaysOfTheAccountNow()
    {
        LinkedAccount linked = await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Nexus.Tier = "Supporter";
        _harness.Time.Advance(TimeSpan.FromDays(3));

        LinkedAccount checkedAccount = await _harness.Service.CheckAsync(TestData.Nexus, Cancellation);

        Assert.Equal("Supporter", checkedAccount.Profile.Tier);
        Assert.Equal((AuthKind.ApiKey, linked.LinkedAt), (checkedAccount.Kind, checkedAccount.LinkedAt));
        Assert.Equal(checkedAccount, Assert.Single(await _harness.Accounts.ListAsync(Cancellation)));
        Assert.Equal(["token-for-good-key", "token-for-good-key"], _harness.Nexus.ProfileTokens);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Information: Checked the account on nexus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CredentialsAboutToExpireAreRefreshedFirst()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));

        await _harness.Service.CheckAsync(TestData.Nexus, Cancellation);

        Assert.Equal(1, _harness.NexusOAuth.RefreshCalls);
        Assert.Equal("access-2", _harness.Nexus.ProfileTokens[^1]);
    }

    [Fact]
    public async Task AnAccountTheSiteRefusesIsKeptAsItWas()
    {
        LinkedAccount linked = await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);
        _harness.Nexus.RefusesProfiles = true;

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _harness.Service.CheckAsync(TestData.Nexus, Cancellation));

        Assert.Equal(linked, Assert.Single(await _harness.Accounts.ListAsync(Cancellation)));
    }

    [Fact]
    public async Task CredentialsThatCantBeUsedAnyMoreArentSent()
    {
        await _harness.LinkAsync(TestData.Nexus, AuthKind.OAuthPkce);
        _harness.NexusOAuth.Refresh = _ => Task.FromResult<AccountCredentials?>(null);
        _harness.Time.Advance(TimeSpan.FromMinutes(59.5));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _harness.Service.CheckAsync(TestData.Nexus, Cancellation));

        Assert.Single(_harness.Nexus.ProfileTokens);
    }

    [Fact]
    public async Task NeedsALinkedAccountOnASourceThatLinksThem()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _harness.Service.CheckAsync(TestData.Nexus, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _harness.Service.CheckAsync(AccountHarness.DirectId, Cancellation));
        Assert.Empty(_harness.Nexus.ProfileTokens);
    }

    [Fact]
    public async Task ListsTheQuotaOfALinkedAccountOnly()
    {
        _harness.Nexus.Quota = Quota;
        _harness.ModIo.Quota = Quota;
        await _harness.LinkAsync(TestData.Nexus, AuthKind.ApiKey);

        IReadOnlyList<SourceAccountStatus> statuses = await _harness.Service.ListAsync(Cancellation);

        Assert.Same(Quota, Assert.Single(statuses, status => status.Source == TestData.Nexus).Quota);
        Assert.Null(Assert.Single(statuses, status => status.Source == TestData.ModIo).Quota);
    }
}
