using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// A site with words of its own for following a mod and approving one, such as subscriptions and likes, is spoken of in
/// them on Browse and on a mod's page; a site with neither shows neither.
/// </summary>
public sealed class BrowseSiteWordsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new();

    public BrowseSiteWordsTests()
    {
        _harness.Catalog.Words = new SiteWords
        {
            Follow = "subscribe",
            Unfollow = "unsubscribe",
            Followed = "subscribed",
            Approval = "like",
            Approvals = "likes",
        };
    }

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task BrowseOrdersAndListsTheFollowedModsInTheSitesWords()
    {
        _harness.Watchlists.Tracked.Add(SkyUi);
        _harness.Catalog.Mods["12604"] = BrowseHarness.Info(SkyUi, "SkyUI");
        await StartAsync();

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal(["Most likes", "Fewest likes"], Screen.Sorts.Where(sort => sort.Sort == SearchSort.Endorsements).Select(sort => sort.Name));
        Assert.Equal((true, "Subscribed mods"), (Screen.CanFollow, Screen.ShowFollowedText));
        Assert.Equal("1 subscribed mod of Skyrim Special Edition on Nexus Mods.", Screen.ResultsText);
        Assert.Equal("1 like", Assert.Single(Screen.Results).EndorsementsText);
    }

    [Fact]
    public async Task FollowedModsTheSiteWontListSayWhyInItsWords()
    {
        await StartAsync();
        _harness.Watchlists.Refusals["GetWatchedAsync"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods couldn't list your subscribed mods: Service unavailable.", Screen.Problem);
    }

    [Fact]
    public async Task AModsPageFollowsItAndCountsItsApprovalInTheSitesWords()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(Liked(87)));

        await mod.ToggleTrackCommand.ExecuteAsync(null);
        string? subscribed = mod.Note;
        ModPageViewModel again = await _harness.OpenAsync(BrowseHarness.Page(Liked(1)));
        await again.ToggleTrackCommand.ExecuteAsync(null);

        Assert.Equal(("Subscribe on Nexus Mods", "Unsubscribe on Nexus Mods", "87 likes"), (mod.TrackText, mod.UntrackText, mod.EndorsementsText));
        Assert.Equal("1 like", again.EndorsementsText);
        Assert.Equal(("Subscribed on Nexus Mods.", "No longer subscribed on Nexus Mods."), (subscribed, again.Note));
    }

    [Fact]
    public async Task FollowingThatFailsSaysSoInTheSitesWords()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(Liked(87)));
        _harness.Watchlists.Refusals["WatchAsync"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await mod.ToggleTrackCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods couldn't change your subscribed mods: Service unavailable.", mod.Note);
    }

    [Fact]
    public async Task ASiteWithoutWordsForFollowingOrApprovalShowsNeither()
    {
        _harness.Catalog.Words = SiteWords.None;
        await StartAsync();

        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(Liked(87)));

        Assert.Equal((false, string.Empty), (Screen.CanFollow, Screen.ShowFollowedText));
        Assert.Equal((false, string.Empty, string.Empty, string.Empty), (mod.CanTrack, mod.TrackText, mod.UntrackText, mod.EndorsementsText));
        Assert.Equal(["Most approvals", "Fewest approvals"], Screen.Sorts.Where(sort => sort.Sort == SearchSort.Endorsements).Select(sort => sort.Name));
    }

    private static ModInfo Liked(long likes)
        => new(SkyUi, "SkyUI", pageUrl: new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604"), endorsements: likes);

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAsync(BrowseHarness.Skyrim);
    }
}
