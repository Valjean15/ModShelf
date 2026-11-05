using ModShelf.Application.Accounts;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.Tests.ViewModels.Accounts;

/// <summary>
/// The accounts at the bottom of the menu: each one linked, with its picture or its initial, its name, and its level and
/// site, in the order of the sites; Link an account with none; following the Accounts screen's cards as they link, check
/// and unlink, and as the screen lists them again when shown. Each opens Accounts.
/// </summary>
public sealed class AccountMenuTests
{
    private static readonly Uri Avatar = new("https://avatars.nexusmods.com/1000001/100");

    private readonly AccountsHarness _harness = new();

    private AccountMenuViewModel Menu => _harness.Menu;

    [Fact]
    public async Task ShowsEachAccountLinkedInTheSitesOrder()
    {
        await _harness.StartAsync(
            AccountsHarness.Account(TestData.Nexus, name: "Kestrel"),
            AccountsHarness.Account(AccountsHarness.ModIo, AuthKind.EmailCode, name: "kes_mods", tier: null));

        Assert.Equal(
            [
                ("Kestrel", "Premium · Nexus Mods", "Kestrel · Premium · Nexus Mods", "K"),
                ("kes_mods", "mod.io", "kes_mods · mod.io", "K"),
            ],
            Menu.Entries.Select(entry => (entry.Name, entry.Level, entry.Tip, entry.Initial)));
        Assert.Equal((true, false), (Menu.HasAccounts, Menu.OffersLinking));
        Assert.NotEqual(Menu.Entries[0].Hue, Menu.Entries[1].Hue);
        Assert.Equal(_harness.Card(TestData.Nexus).Hue, Menu.Entries[0].Hue);
    }

    [Fact]
    public async Task OffersLinkingWithNoAccountLinked()
    {
        await _harness.StartAsync();

        Assert.Empty(Menu.Entries);
        Assert.Equal((false, true), (Menu.HasAccounts, Menu.OffersLinking));
    }

    [Fact]
    public async Task OffersLinkingWithOneSiteToo()
    {
        _harness.Accounts.AddSource(TestData.Nexus, AccountsHarness.NexusName, worksWithoutAccount: false, [AuthKind.ApiKey]);

        await _harness.Screen.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(Menu.OffersLinking);
    }

    [Fact]
    public void SaysNothingBeforeTheSitesAreListed()
    {
        Assert.Equal((false, false), (Menu.HasAccounts, Menu.OffersLinking));
    }

    [Fact]
    public async Task ThePictureShowsOnceReadOnTheMenuAndTheCard()
    {
        _harness.Pictures.Cache.Held = new TaskCompletionSource();
        await _harness.StartAsync(AccountsHarness.Account(TestData.Nexus, picture: Avatar));
        AccountMenuEntryViewModel entry = Assert.Single(Menu.Entries);
        AccountViewModel card = _harness.Card(TestData.Nexus);
        (bool, bool) reading = (entry.Picture.HasFile, card.Picture.HasFile);

        _harness.Pictures.Cache.Held.SetResult();
        await entry.Picture.Showing;
        await card.Picture.Showing;

        Assert.Equal((false, false), reading);
        Assert.Equal((ScriptedPictureCache.FileOf(Avatar), ScriptedPictureCache.FileOf(Avatar)), (entry.Picture.File, card.Picture.File));
        Assert.Equal("J", card.Initial);
    }

    [Fact]
    public async Task APictureThatCantBeReadLeavesTheInitial()
    {
        _harness.Pictures.Cache.Missing[Avatar] = true;

        await _harness.StartAsync(AccountsHarness.Account(TestData.Nexus, picture: Avatar));
        AccountMenuEntryViewModel entry = Assert.Single(Menu.Entries);
        await entry.Picture.Showing;

        Assert.Equal(((string?)null, "J"), (entry.Picture.File, entry.Initial));
    }

    [Fact]
    public async Task FollowsTheCardsAsTheyLinkCheckAndUnlink()
    {
        await _harness.StartAsync();
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.Accounts.Link = (source, _, _, _) => Task.FromResult(AccountsHarness.Account(source, tier: "Free"));

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);
        string linked = Assert.Single(Menu.Entries).Level;
        _harness.Accounts.Check = (source, _) => Task.FromResult(AccountsHarness.Account(source, name: "Jean2", tier: "Premium", picture: Avatar));
        await nexus.CheckCommand.ExecuteAsync(null);
        AccountMenuEntryViewModel checkedEntry = Assert.Single(Menu.Entries);
        (string, string) checkedAccount = (checkedEntry.Name, checkedEntry.Level);
        _harness.Dialogs.Confirm.Enqueue(true);
        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.Equal("Free · Nexus Mods", linked);
        Assert.Equal(("Jean2", "Premium · Nexus Mods"), checkedAccount);
        Assert.Contains(Avatar, _harness.Pictures.Cache.Asked);
        Assert.Empty(Menu.Entries);
        Assert.True(Menu.OffersLinking);
    }

    [Fact]
    public async Task TheScreenListsThemAgainWhenShown()
    {
        await _harness.StartAsync();

        _harness.Accounts.SetAccount(AccountsHarness.ModIo, AccountsHarness.Account(AccountsHarness.ModIo, AuthKind.EmailCode, name: "kes_mods", tier: "Member"));
        _harness.Screen.OnShown();
        await _harness.Screen.Refreshing;

        Assert.True(_harness.Card(AccountsHarness.ModIo).IsLinked);
        Assert.Equal(["kes_mods"], Menu.Entries.Select(entry => entry.Name));
        Assert.Equal(["list", "list"], _harness.Accounts.Calls);
    }

    [Fact]
    public async Task ACardBusyIsLeftAsItIsWhenTheScreenListsThemAgain()
    {
        await _harness.StartAsync(AccountsHarness.Account(TestData.Nexus));
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        TaskCompletionSource<LinkedAccount> checking = new();
        _harness.Accounts.Check = (_, _) => checking.Task;
        Task check = nexus.CheckCommand.ExecuteAsync(null);

        _harness.Accounts.SetAccount(TestData.Nexus, null);
        _harness.Screen.OnShown();
        await _harness.Screen.Refreshing;
        bool linked = nexus.IsLinked;
        checking.SetResult(AccountsHarness.Account(TestData.Nexus));
        await check;

        Assert.True(linked);
        Assert.Single(Menu.Entries);
    }

    [Fact]
    public async Task ListingAgainThatFailsIsLoggedAndLeavesThem()
    {
        await _harness.StartAsync(AccountsHarness.Account(TestData.Nexus));
        _harness.Accounts.ListRefusal = new InvalidDataException("The database can't be read.");

        _harness.Screen.OnShown();
        await _harness.Screen.Refreshing;

        Assert.True(_harness.Card(TestData.Nexus).IsLinked);
        Assert.Single(Menu.Entries);
        Assert.StartsWith("Error: The linked accounts couldn't be listed System.IO.InvalidDataException", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShownBeforeTheScreenListedThemItListsNothingMore()
    {
        _harness.Screen.OnShown();
        await _harness.Screen.Refreshing;

        Assert.Empty(_harness.Accounts.Calls);
    }

    [Fact]
    public async Task EachOpensAccounts()
    {
        await _harness.StartAsync(AccountsHarness.Account(TestData.Nexus));

        Menu.OpenAccountsCommand.Execute(null);

        Assert.Equal(Screen.Accounts, _harness.Navigation.Current);
    }
}
