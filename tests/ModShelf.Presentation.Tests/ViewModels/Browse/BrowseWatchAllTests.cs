using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Watch all, over the mods of a game the account follows: each one the library doesn't watch yet, watched for updates;
/// and a site whose followed mods ModShelf can only read, as GameBanana's subscriptions, listed without a way to change
/// them.
/// </summary>
public sealed class BrowseWatchAllTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("32444");
    private static readonly ModRef Ussep = TestData.Mod("266");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly BrowseHarness _harness = new();

    public BrowseWatchAllTests()
    {
        _harness.Watchlists.Tracked.UnionWith([SkyUi, Address, Ussep, Smapi]);
        _harness.Catalog.Mods["12604"] = BrowseHarness.Info(SkyUi, "SkyUI");
        _harness.Catalog.Mods["32444"] = BrowseHarness.Info(Address, "Address Library");
        _harness.Catalog.Mods["266"] = BrowseHarness.Info(Ussep, "USSEP");
    }

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OffersToWatchTheGamesFollowedModsTheLibraryDoesntWatch()
    {
        await StartAsync(
            TestData.LibraryMod(SkyUi, "SkyUI"),
            TestData.LibraryMod(Address, "Address Library", watched: false, TestData.LibraryFile(Address, "1", "address.7z")));
        bool before = Screen.CanWatchAll;

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.False(before);
        Assert.Equal(("Watch all (2)", true), (Screen.WatchAllText, Screen.CanWatchAll));
    }

    [Fact]
    public async Task WatchesEachOneAndSaysHowMany()
    {
        await StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        await Screen.WatchAllCommand.ExecuteAsync(null);

        Assert.Equal(["Watch from the site 266", "Watch from the site 32444"], _harness.Library.Asked.Where(asked => asked.StartsWith("Watch from", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(("Watching 2 more mods for updates in the library.", false), (Screen.SelectionNote, Screen.IsSelectionNoteAProblem));
        Assert.Equal(("Watch all (0)", false), (Screen.WatchAllText, Screen.CanWatchAll));
        Assert.All(Screen.Results, row => Assert.True(row.IsWatched));
    }

    [Fact]
    public async Task SaysWhatItCouldntWatch()
    {
        await StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"));
        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        _harness.Library.Refusal = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await Screen.WatchAllCommand.ExecuteAsync(null);

        Assert.Equal(("Watching 0 more mods for updates in the library. 1 couldn't be watched: Service unavailable.", true), (Screen.SelectionNote, Screen.IsSelectionNoteAProblem));
        Assert.True(Screen.CanWatchAll);
    }

    [Fact]
    public async Task SaysWhenItWatchedOne()
    {
        await StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"));
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        await Screen.WatchAllCommand.ExecuteAsync(null);

        Assert.Equal("Watching 1 more mod for updates in the library.", Screen.SelectionNote);
    }

    [Fact]
    public async Task SaysWhyTheSiteRefusedAMod()
    {
        await StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"));
        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        _harness.Library.Refusal = new ArgumentException("The site has no such mod.");

        await Screen.WatchAllCommand.ExecuteAsync(null);

        Assert.Equal("Watching 0 more mods for updates in the library. 1 couldn't be watched: The site has no such mod.", Screen.SelectionNote);
    }

    [Fact]
    public async Task AsksFirstWhenWatchingThemLikelyUsesUpTheRequests()
    {
        await StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromDays(1), 1000, 1, TestData.Now.AddHours(5))]);

        await Screen.WatchAllCommand.ExecuteAsync(null);

        Assert.StartsWith("This may take about 2 requests to Nexus Mods, and 1 are left today.", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Library.Asked, asked => asked.StartsWith("Watch from", StringComparison.Ordinal));
        Assert.Equal(("Watch all (2)", true), (Screen.WatchAllText, Screen.CanWatchAll));
    }

    [Fact]
    public async Task ASearchHasNoWatchAll()
    {
        await StartAsync();
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Screen.SearchText = "sky";
        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.False(Screen.CanWatchAll);
    }

    [Fact]
    public async Task ASiteWhoseFollowedModsItCanOnlyReadListsThemWithoutAWayToChangeThem()
    {
        _harness.Watchlists.ReadOnly.Add(TestData.Nexus);
        _harness.Catalog.Words = new SiteWords { Followed = "subscribed" };
        await StartAsync();

        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        ModPageViewModel page = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.Equal((true, "Subscribed mods"), (Screen.CanFollow, Screen.ShowFollowedText));
        Assert.Equal("3 subscribed mods of Skyrim Special Edition on Nexus Mods.", Screen.ResultsText);
        Assert.Equal("Watch all (3)", Screen.WatchAllText);
        Assert.False(page.CanTrack);
    }

    private async Task StartAsync(params Domain.Library.LibraryMod[] mods)
    {
        await _harness.StartAsync(mods);
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }
}
