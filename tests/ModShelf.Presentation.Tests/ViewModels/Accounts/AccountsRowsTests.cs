using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Accounts;

/// <summary>
/// Settings › Accounts' compact rows, grouped by need, and the accounts that need attention, which Settings' badge counts
/// (ADR 0094).
/// </summary>
public sealed class AccountsRowsTests
{
    private readonly AccountsHarness _harness = new();

    [Fact]
    public async Task GroupsTheSitesByWhetherTheyNeedAnAccount()
    {
        await _harness.StartAsync();

        Assert.Equal([TestData.Nexus], _harness.Screen.NeedOne.Select(row => row.Source));
        Assert.Equal([AccountsHarness.ModIo], _harness.Screen.Optional.Select(row => row.Source));
        Assert.Equal(
            [
                ("DEG Mods", "ModShelf searches its mods, downloads its files and checks them for updates, with no account."),
                ("Web links", "ModShelf downloads its files, with no account."),
            ],
            _harness.Screen.NoneNeeded.Select(site => (site.SiteName, site.AboutText)));
        Assert.Equal((true, true, true), (_harness.Screen.HasNeedOne, _harness.Screen.HasOptional, _harness.Screen.HasNoneNeeded));
    }

    [Fact]
    public async Task SaysWhatModShelfDoesOnEachSiteThatLinksNoAccount()
    {
        AccountsHarness harness = new(sites: [new LinkingSite(TestData.Nexus, AccountsHarness.NexusName), new FeedSite(new SourceId("feeds"), "Feeds")]);

        await harness.StartAsync();

        Assert.Equal(
            [("Feeds", "ModShelf downloads its files and checks them for updates, with no account.")],
            harness.Screen.NoneNeeded.Select(site => (site.SiteName, site.AboutText)));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task ShowsOnlyTheGroupsWithSites(bool worksWithoutAccount, bool needOne, bool optional)
    {
        AccountsHarness harness = new(openSites: false);
        harness.Accounts.AddSource(AccountsHarness.ModIo, AccountsHarness.ModIoName, worksWithoutAccount, [AuthKind.EmailCode]);

        await harness.Screen.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((needOne, optional, false), (harness.Screen.HasNeedOne, harness.Screen.HasOptional, harness.Screen.HasNoneNeeded));
    }

    [Fact]
    public async Task ARowSaysTheAccountAndWhatsLeftOfItsRequestsOrThatNoneIsLinked()
    {
        using CultureScope culture = new("en-US");
        _harness.Accounts.Quotas[TestData.Nexus] = new ApiQuota(
        [
            new QuotaAllowance(TimeSpan.FromDays(1), 20000, 19998, TestData.Now.AddDays(1)),
            new QuotaAllowance(TimeSpan.FromHours(1), 500, 120, TestData.Now.AddHours(1)),
        ]);

        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus, name: "Kestrel"));

        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        AccountViewModel modIo = _harness.Card(AccountsHarness.ModIo);
        Assert.Equal(("Kestrel · 120 of 500 requests left this hour", false), (nexus.SummaryText, nexus.ShowsLink));
        Assert.Equal(("Not linked", true, null), (modIo.SummaryText, modIo.ShowsLink, modIo.RequestsText));
    }

    [Fact]
    public async Task LinkOpensTheRowAndDetailsOpensAndClosesIt()
    {
        await _harness.StartAsync();
        AccountViewModel modIo = _harness.Card(AccountsHarness.ModIo);
        (bool, string) closed = (modIo.IsExpanded, modIo.DetailsText);

        modIo.ExpandCommand.Execute(null);
        (bool, bool, string) opened = (modIo.IsExpanded, modIo.ShowsLink, modIo.DetailsText);
        modIo.ToggleDetailsCommand.Execute(null);

        Assert.Equal((false, "Details"), closed);
        Assert.Equal((true, false, "Less"), opened);
        Assert.Equal((false, true), (modIo.IsExpanded, modIo.ShowsLink));
    }

    [Fact]
    public async Task ASiteWhoseDownloadsWaitForAnAccountNeedsAttention()
    {
        await _harness.StartAsync(modIo: AccountsHarness.Account(AccountsHarness.ModIo));
        _harness.Wanting.Show([TestData.Nexus, AccountsHarness.ModIo]);
        _harness.UiThread.RunPending();

        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        AccountViewModel modIo = _harness.Card(AccountsHarness.ModIo);
        Assert.Equal("Downloads wait for an account", nexus.AttentionText);
        Assert.Equal("Downloads failed for want of an account on mod.io: check it, or link it again.", modIo.AttentionText);
        Assert.Equal(2, _harness.Screen.Badge);

        _harness.Wanting.Show([]);
        _harness.UiThread.RunPending();

        Assert.Equal((false, false, 0), (nexus.NeedsAttention, modIo.NeedsAttention, _harness.Screen.Badge));
    }

    [Fact]
    public async Task ASiteThatRefusedTheAccountNeedsAttentionUntilItsCheckedOrLinkedAgain()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.Accounts.Check = (_, _) => Task.FromException<LinkedAccount>(new AuthenticationFailedException("The key was revoked."));

        await nexus.CheckCommand.ExecuteAsync(null);
        (string?, int) refused = (nexus.AttentionText, _harness.Screen.Badge);
        _harness.Accounts.Check = (source, _) => Task.FromResult(AccountsHarness.Account(source));
        await nexus.CheckCommand.ExecuteAsync(null);

        Assert.Equal(("Nexus Mods refused the account: link it again.", 1), refused);
        Assert.Equal((null, 0), (nexus.AttentionText, _harness.Screen.Badge));
    }

    [Fact]
    public async Task UnlinkingARefusedAccountLeavesNothingToLookAt()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.Accounts.Check = (_, _) => Task.FromException<LinkedAccount>(new AuthenticationFailedException("The key was revoked."));
        await nexus.CheckCommand.ExecuteAsync(null);
        _harness.Dialogs.Confirm.Enqueue(true);

        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.Equal((null, 0), (nexus.AttentionText, _harness.Screen.Badge));
    }

    [Fact]
    public async Task ALinkThatWorksAfterARefusalLeavesNothingToLookAt()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.Accounts.Check = (_, _) => Task.FromException<LinkedAccount>(new AuthenticationFailedException("The key was revoked."));
        await nexus.CheckCommand.ExecuteAsync(null);
        _harness.Accounts.Link = (source, kind, _, _) => Task.FromResult(AccountsHarness.Account(source, kind));

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.Equal((null, 0), (nexus.AttentionText, _harness.Screen.Badge));
    }
}
