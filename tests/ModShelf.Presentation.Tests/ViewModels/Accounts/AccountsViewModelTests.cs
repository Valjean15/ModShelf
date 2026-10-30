using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Accounts;

public sealed class AccountsViewModelTests
{
    private readonly AccountsHarness _harness = new();

    /// <summary>
    /// What en-US puts between a time and AM or PM, which depends on the system's culture data: a space, or a narrow one.
    /// </summary>
    private static string AmPm => new DateTime(2026, 1, 1, 13, 0, 0).ToString("t", System.Globalization.CultureInfo.GetCultureInfo("en-US"))[4..^2];

    [Fact]
    public async Task ShowsACardPerSiteWithoutAskingTheSites()
    {
        using CultureScope culture = new("en-US");

        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus, linkedAt: new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero)));

        Assert.Equal([TestData.Nexus, AccountsHarness.ModIo], _harness.Screen.Sources.Select(card => card.Source));
        Assert.False(_harness.Screen.IsLoading);
        Assert.Null(_harness.Screen.EmptyText);
        Assert.Equal(["list"], _harness.Accounts.Calls);

        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        Assert.Equal(AccountsHarness.NexusName, nexus.SiteName);
        Assert.Equal("Nexus Mods needs a linked account.", nexus.AboutText);
        Assert.True(nexus.IsLinked);
        Assert.Equal(("Jean", "Premium", "Linked on 10/20/2026"), (nexus.AccountName, nexus.Tier, nexus.LinkedText));
        Assert.Equal(
            [(AuthKind.ApiKey, "Link with an API key", true, false), (AuthKind.OAuthPkce, "Link in the browser", false, false)],
            nexus.LinkMethods.Select(method => (method.Kind, method.Label, method.IsFirst, method.IsMain)));
        Assert.True(nexus.CheckCommand.CanExecute(null));
        Assert.True(nexus.UnlinkCommand.CanExecute(null));

        AccountViewModel modIo = _harness.Card(AccountsHarness.ModIo);
        Assert.Equal("Linking an account on mod.io is optional.", modIo.AboutText);
        Assert.False(modIo.IsLinked);
        Assert.Equal((string.Empty, null, "No account is linked."), (modIo.AccountName, modIo.Tier, modIo.LinkedText));
        Assert.Equal([(AuthKind.EmailCode, "Link with an emailed code", true, true)], modIo.LinkMethods.Select(method => (method.Kind, method.Label, method.IsFirst, method.IsMain)));
        Assert.False(modIo.CheckCommand.CanExecute(null));
        Assert.False(modIo.UnlinkCommand.CanExecute(null));
        Assert.All(_harness.Screen.Sources.SelectMany(card => card.LinkMethods), method => Assert.True(method.Command.CanExecute(null)));
    }

    [Fact]
    public async Task ShowsTheDateTheAccountWasLinkedWhereTheUserIs()
    {
        using CultureScope culture = new("en-US");
        _harness.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14"));

        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus, linkedAt: new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero)));

        Assert.Equal("Linked on 10/21/2026", _harness.Card(TestData.Nexus).LinkedText);
    }

    [Fact]
    public async Task ShowsWhatsLeftOfALinkedAccountsApiRequests()
    {
        using CultureScope culture = new("en-US");
        ApiQuota quota = new(
        [
            new QuotaAllowance(TimeSpan.FromDays(1), 20000, 19998, new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)),
            new QuotaAllowance(TimeSpan.FromHours(1), 2000, 1998, new DateTimeOffset(2026, 10, 27, 13, 0, 0, TimeSpan.Zero)),
            new QuotaAllowance(TimeSpan.FromMinutes(15), 100, 0, new DateTimeOffset(2026, 10, 27, 12, 15, 0, TimeSpan.Zero)),
        ]);
        _harness.Accounts.Quotas[TestData.Nexus] = quota;
        _harness.Accounts.Quotas[AccountsHarness.ModIo] = quota;

        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));

        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        Assert.True(nexus.HasQuota);
        Assert.Equal("API requests left, as Nexus Mods last said:", nexus.QuotaHeading);
        Assert.Equal(
            [
                $"Per day: 19,998 of 20,000 left, until 10/28/2026 12:00{AmPm}AM",
                $"Per hour: 1,998 of 2,000 left, until 10/27/2026 1:00{AmPm}PM",
                $"Per 0:15:00: 0 of 100 left, until 10/27/2026 12:15{AmPm}PM",
            ],
            nexus.Quota);

        // Without an account, the quota isn't the user's.
        Assert.False(_harness.Card(AccountsHarness.ModIo).HasQuota);
        Assert.Empty(_harness.Card(AccountsHarness.ModIo).Quota);
    }

    [Fact]
    public async Task SaysWhenNoSiteSupportsAccounts()
    {
        await _harness.Screen.StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_harness.Screen.Sources);
        Assert.Equal(Strings.AccountsNone, _harness.Screen.EmptyText);
        Assert.False(_harness.Screen.IsEmptyTextAProblem);
        Assert.False(_harness.Screen.IsLoading);
    }

    [Fact]
    public async Task SaysWhenTheAccountsCantBeListed()
    {
        _harness.Accounts.ListRefusal = new InvalidOperationException("The database is locked.");

        await _harness.StartAsync();

        Assert.Empty(_harness.Screen.Sources);
        Assert.Equal(Strings.SomethingWentWrong, _harness.Screen.EmptyText);
        Assert.True(_harness.Screen.IsEmptyTextAProblem);
        Assert.False(_harness.Screen.IsLoading);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: The linked accounts couldn't be listed System.InvalidOperationException: The database is locked.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListsOnTheUiThread()
    {
        _harness.UiThread.Inline = false;

        await _harness.StartAsync();
        bool listedBefore = _harness.Screen.Sources.Count > 0 || !_harness.Screen.IsLoading;
        _harness.UiThread.RunPending();

        Assert.False(listedBefore);
        Assert.Equal(2, _harness.Screen.Sources.Count);
        Assert.False(_harness.Screen.IsLoading);
    }

    [Fact]
    public async Task StoppingWhileListingIsQuiet()
    {
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();
        _harness.Accounts.ListRefusal = new OperationCanceledException(stopping.Token);

        await _harness.Screen.StartAsync(stopping.Token);

        Assert.Null(_harness.Screen.EmptyText);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WarnsWhenNoKeyringProtectsTheCredentials(bool protectedByOs, bool warns)
    {
        AccountsHarness harness = new(protectedByOs);

        Assert.Equal(warns, harness.Screen.IsStoreUnprotected);
    }

    [Fact]
    public void IsLoadingUntilStarted()
    {
        Assert.True(_harness.Screen.IsLoading);
        Assert.Empty(_harness.Screen.Sources);
        Assert.Null(_harness.Screen.EmptyText);
    }
}
