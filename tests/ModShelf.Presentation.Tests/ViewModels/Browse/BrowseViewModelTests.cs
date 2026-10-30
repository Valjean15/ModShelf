using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

public sealed class BrowseViewModelTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ReadsTheGamesTheFirstTimeTheScreenIsShown()
    {
        await _harness.StartAsync();
        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Downloads);
        bool askedBeforeShown = _harness.Lookup.Asked.Count > 0;

        await _harness.ShowAsync();
        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Library);
        await _harness.ShowAsync();

        Assert.False(askedBeforeShown);
        Assert.Equal(["Fallout 4", "Skyrim Special Edition", "Stardew Valley"], Screen.Games.Select(game => game.Name));
        Assert.Equal(["GetGamesAsync nexus"], _harness.Lookup.Asked);
        Assert.False(Screen.GamesFailed);
    }

    [Fact]
    public async Task GamesAreReadAtOnceWhenTheAppStartsOnTheScreen()
    {
        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Browse);

        await _harness.StartAsync();
        await Screen.GamesWork;

        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task ChoosesTheGameTheLibraryHasTheMostModsOfAndSearchesIt()
    {
        _harness.Searchable(BrowseHarness.Stardew, 3);
        await _harness.StartAsync(
            TestData.LibraryMod(TestData.Mod("1"), "A"),
            TestData.LibraryMod(TestData.Mod("2", "stardewvalley"), "B"),
            TestData.LibraryMod(TestData.Mod("3", "stardewvalley"), "C"));

        await _harness.ShowAsync();

        Assert.Equal(BrowseHarness.Stardew.Game, Screen.Game?.Game);
        (GameRef game, SearchQuery query) = Assert.Single(_harness.Lookup.Searches);
        Assert.Equal(BrowseHarness.Stardew.Game, game);
        Assert.Equal(new SearchQuery(count: BrowseViewModel.PageSize), query);
        Assert.Equal(["Mod 1", "Mod 2", "Mod 3"], _harness.Names());
        Assert.Equal("3 mods match.", Screen.ResultsText);
        Assert.False(Screen.HasMore);
    }

    [Fact]
    public async Task WithAnEmptyLibraryNoGameIsChosen()
    {
        await _harness.StartAsync();

        await _harness.ShowAsync();

        Assert.Null(Screen.Game);
        Assert.Empty(_harness.Lookup.Searches);
        Assert.Equal(string.Empty, Screen.ResultsText);
    }

    [Fact]
    public async Task WithSeveralSitesEachGameNamesItsSite()
    {
        SourceId other = new("other");
        _harness.Lookup.Searchable.Add(other);
        _harness.Lookup.Games[other] = [new GameInfo(new GameRef(other, "skyrim"), "Skyrim Special Edition")];
        await _harness.StartAsync();

        await _harness.ShowAsync();

        Assert.Equal(
            ["Fallout 4 (Nexus Mods)", "Skyrim Special Edition (Nexus Mods)", "Skyrim Special Edition (other)", "Stardew Valley (Nexus Mods)"],
            Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task ChoosingAGameSearchesIt()
    {
        _harness.Searchable(BrowseHarness.Skyrim, 1);
        await _harness.StartAsync();
        await _harness.ShowAsync();

        Screen.Game = Screen.Games[1];
        await Screen.Listing;

        Assert.Equal(["Mod 1"], _harness.Names());
        Assert.Equal("1 mod matches.", Screen.ResultsText);
    }

    [Fact]
    public async Task SearchesForTheWordsAuthorOrderAndContentAsked()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);
        Screen.SearchText = "  sky ui ";
        Screen.Author = "schlangster";
        Screen.SelectedSort = Screen.Sorts[4];
        Screen.IncludeAdultContent = true;

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal(
            new SearchQuery("sky ui", SearchSort.Downloads, 0, BrowseViewModel.PageSize, "schlangster", includeAdultContent: true, SearchDirection.Ascending),
            _harness.Lookup.Searches[^1].Query);
        Assert.Equal("No mods match.", Screen.ResultsText);
    }

    [Fact]
    public async Task OffersEachOrderOnce()
    {
        await _harness.StartAsync();

        Assert.Equal(9, Screen.Sorts.Select(sort => (sort.Sort, sort.Direction)).Distinct().Count());
        Assert.Equal(("Best match", SearchSort.Relevance), (Screen.SelectedSort.Name, Screen.SelectedSort.Sort));
        Assert.Equal("Least downloaded", Screen.Sorts[4].ToString());
    }

    [Fact]
    public async Task ChangingTheOrderOrTheAdultContentSearchesAgain()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 30);
        await Screen.LoadMoreCommand.ExecuteAsync(null);

        Screen.SelectedSort = Screen.Sorts[1];
        await Screen.Listing;
        Screen.IncludeAdultContent = true;
        await Screen.Listing;

        Assert.Equal(
            [(0, SearchSort.Relevance, false), (20, SearchSort.Relevance, false), (0, SearchSort.Endorsements, false), (0, SearchSort.Endorsements, true)],
            _harness.Lookup.Searches.Select(search => (search.Query.Offset, search.Query.Sort, search.Query.IncludeAdultContent)));
        Assert.Equal(20, Screen.Results.Count);
    }

    [Fact]
    public async Task ChangingTheOrderBeforeAnySearchSearchesNothing()
    {
        await _harness.StartAsync();

        Screen.SelectedSort = Screen.Sorts[1];
        Screen.IncludeAdultContent = true;

        Assert.Empty(_harness.Lookup.Searches);
    }

    [Fact]
    public async Task LoadsMorePagesAfterTheFirst()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 45);
        bool moreAtFirst = Screen.HasMore;

        await Screen.LoadMoreCommand.ExecuteAsync(null);
        await Screen.LoadMoreCommand.ExecuteAsync(null);

        Assert.True(moreAtFirst);
        Assert.Equal([0, 20, 40], _harness.Lookup.Searches.Select(search => search.Query.Offset));
        Assert.Equal(Enumerable.Range(1, 45).Select(id => $"Mod {id}"), _harness.Names());
        Assert.False(Screen.HasMore);
        Assert.False(Screen.LoadMoreCommand.CanExecute(null));
        Assert.Equal("45 mods match.", Screen.ResultsText);
    }

    [Fact]
    public async Task ModsListedAlreadyArentListedAgain()
    {
        List<ModInfo> mods = await ChooseAsync(BrowseHarness.Skyrim, 30);

        // A mod new to the site moves the rest down a place, so the next page starts with the last one listed.
        mods.Insert(0, BrowseHarness.Info(TestData.Mod("99"), "New mod"));
        await Screen.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(Enumerable.Range(1, 30).Select(id => $"Mod {id}"), _harness.Names());
    }

    [Fact]
    public async Task SearchingWithoutAGameSaysToChooseOne()
    {
        await _harness.StartAsync();
        Screen.SearchText = "SkyUI";

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Choose a game to search its mods.", Screen.Problem);
        Assert.Empty(_harness.Lookup.Searches);
    }

    [Fact]
    public async Task ASearchTheSiteRefusesSaysWhyAndKeepsTheList()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 2);
        _harness.Lookup.Refusals["SearchAsync"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        Screen.SearchText = "SkyUI";

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods couldn't search: Service unavailable.", Screen.Problem);
        Assert.False(Screen.NeedsAccount);
        Assert.Equal(["Mod 1", "Mod 2"], _harness.Names());
        Assert.False(Screen.IsBusy);
    }

    [Fact]
    public async Task ASiteThatNeedsAnAccountLeadsToTheAccountsScreen()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);
        _harness.Lookup.Refusals["SearchAsync"] = new AuthenticationFailedException("No account is linked.");

        await Screen.SearchCommand.ExecuteAsync(null);
        Screen.GoToAccountsCommand.Execute(null);

        Assert.Equal(
            "Nexus Mods needs your account linked, or refused the one linked. Link it on the Accounts screen, then try again.",
            Screen.Problem);
        Assert.True(Screen.NeedsAccount);
        Assert.Equal(Presentation.Services.Screen.Accounts, _harness.Navigation.Current);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLogged()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);
        _harness.Lookup.Refusals["SearchAsync"] = new InvalidOperationException("Something broke.");

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Something went wrong. The log has the details.", Screen.Problem);
        Assert.StartsWith("Error: Listing or opening mods failed System.InvalidOperationException", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GamesThatCantBeReadSayWhyAndCanBeReadAgain()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new AuthenticationFailedException("No account is linked.");
        await _harness.StartAsync();
        await _harness.ShowAsync();
        (bool failed, bool needsAccount, bool gamesProblem, bool canRetry) =
            (Screen.GamesFailed, Screen.NeedsAccount, Screen.IsGamesProblem, Screen.ReadGamesCommand.CanExecute(null));

        await Screen.ReadGamesCommand.ExecuteAsync(null);

        Assert.Equal((true, true, true, true), (failed, needsAccount, gamesProblem, canRetry));
        Assert.Equal(3, Screen.Games.Count);
        Assert.Equal((null, false), (Screen.Problem, Screen.IsGamesProblem));
        Assert.False(Screen.ReadGamesCommand.CanExecute(null));
    }

    [Fact]
    public async Task AProblemAfterTheGamesOnesIsntTheGames()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new HttpRequestException("No route to host.");
        await _harness.StartAsync();
        await _harness.ShowAsync();

        Screen.SearchText = "https://example.com/somewhere";
        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal("No site ModShelf knows has mods at that link.", Screen.Problem);
        Assert.False(Screen.IsGamesProblem);
        Assert.True(Screen.ReadGamesCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowingTheScreenAgainReadsGamesThatCouldntBeRead()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new HttpRequestException("No route to host.");
        await _harness.StartAsync();
        await _harness.ShowAsync();
        string? problem = Screen.Problem;

        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Library);
        await _harness.ShowAsync();

        Assert.Equal("Couldn't read the games from Nexus Mods: No route to host.", problem);
        Assert.Equal(["GetGamesAsync nexus", "GetGamesAsync nexus"], _harness.Lookup.Asked);
        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task WithNoSiteThatCanSearchItSaysSo()
    {
        _harness.Lookup.Searchable.Clear();
        await _harness.StartAsync();

        await _harness.ShowAsync();

        Assert.Equal("No site ModShelf knows can search its mods.", Screen.Problem);
        Assert.Empty(Screen.Games);
    }

    [Fact]
    public async Task ANewSearchDropsTheOneUnderWay()
    {
        _harness.Lookup.Mods[BrowseHarness.Skyrim.Game] =
        [
            BrowseHarness.Info(TestData.Mod("1"), "The first mod"),
            BrowseHarness.Info(TestData.Mod("2"), "The second mod"),
        ];
        await ChooseAsync(BrowseHarness.Skyrim, -1);
        TaskCompletionSource first = new();
        TaskCompletionSource second = new();
        _harness.Lookup.HeldSearches.Enqueue(first);
        _harness.Lookup.HeldSearches.Enqueue(second);
        Screen.SearchText = "first";
        Task firstSearch = Screen.SearchCommand.ExecuteAsync(null);

        Screen.SearchText = "second";
        Task secondSearch = Screen.SearchCommand.ExecuteAsync(null);
        first.SetResult();
        await firstSearch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        (bool busy, IReadOnlyList<string> names) = (Screen.IsBusy, _harness.Names());
        second.SetResult();
        await secondSearch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(busy);
        Assert.Equal(["The first mod", "The second mod"], names);
        Assert.Equal(["first", "second"], _harness.Lookup.Searches.Skip(1).Select(search => search.Query.Text));
        Assert.Equal(["The second mod"], _harness.Names());
        Assert.Null(Screen.Problem);
        Assert.False(Screen.IsBusy);
    }

    [Fact]
    public async Task WordsThatLookLikeALinkWithoutSlashesAreSearched()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);
        Screen.SearchText = "skse:plugins";

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal("skse:plugins", _harness.Lookup.Searches[^1].Query.Text);
        Assert.DoesNotContain(_harness.Lookup.Asked, asked => asked.StartsWith("LookUpAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASearchThatWorksClearsTheProblemBefore()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 1);
        _harness.Lookup.Refusals["SearchAsync"] = new AuthenticationFailedException("No account is linked.");
        await Screen.SearchCommand.ExecuteAsync(null);

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal((null, false), (Screen.Problem, Screen.NeedsAccount));
        Assert.Equal(["Mod 1"], _harness.Names());
    }

    [Fact]
    public async Task ReadingTheGamesAgainKeepsTheGameChosen()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new HttpRequestException("No route to host.");
        await _harness.StartAsync(TestData.LibraryMod(TestData.Mod("1"), "A"));
        await _harness.ShowAsync();
        Screen.Game = new GameChoice(BrowseHarness.Fallout.Game, "Fallout 4");
        await Screen.Listing;

        await Screen.ReadGamesCommand.ExecuteAsync(null);

        Assert.Equal(BrowseHarness.Fallout.Game, Screen.Game?.Game);
        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task ChangingTheOrderWhileTheTrackedModsShowListsNothingElse()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 1);
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Screen.SelectedSort = Screen.Sorts[1];
        Screen.IncludeAdultContent = true;

        Assert.True(Screen.IsShowingTracked);
        Assert.Single(_harness.Lookup.Searches);
    }

    [Fact]
    public async Task PastingALinkOpensItsModAndBackReturnsToTheList()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 2);
        ModPage page = BrowseHarness.Page(BrowseHarness.Info(TestData.Mod("12604"), "SkyUI"));

        ModPageViewModel opened = await _harness.OpenAsync(page);
        bool open = Screen.IsModOpen;
        Screen.BackCommand.Execute(null);

        Assert.Equal("SkyUI", opened.Name);
        Assert.True(open);
        Assert.Null(Screen.Mod);
        Assert.False(Screen.BackCommand.CanExecute(null));
        Assert.Equal(["Mod 1", "Mod 2"], _harness.Names());
        Assert.Single(_harness.Lookup.Searches);
    }

    [Fact]
    public async Task AGameChosenWithALinkInTheBoxIsSearchedWithoutWords()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);
        await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(TestData.Mod("12604"), "SkyUI")));

        Screen.Game = Assert.Single(Screen.Games, game => game.Game == BrowseHarness.Stardew.Game);
        await Screen.Listing;

        Assert.Equal((BrowseHarness.Stardew.Game, (string?)null), (_harness.Lookup.Searches[^1].Game, _harness.Lookup.Searches[^1].Query.Text));
    }

    [Theory]
    [InlineData("https://example.com/somewhere", "No site ModShelf knows has mods at that link.")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604", "The site needs a linked account to look that link up, or refused the one linked. Link it on the Accounts screen, then try again.")]
    public async Task ALinkThatCantBeOpenedSaysWhy(string link, string problem)
    {
        await _harness.StartAsync();
        if (link.Contains("nexusmods", StringComparison.Ordinal))
        {
            _harness.Lookup.Refusals["LookUpAsync"] = new AuthenticationFailedException("No account is linked.");
        }

        Screen.SearchText = link;
        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Equal(problem, Screen.Problem);
        Assert.Null(Screen.Mod);
    }

    [Fact]
    public async Task OpeningAModOfTheListReadsItFromItsSite()
    {
        List<ModInfo> mods = await ChooseAsync(BrowseHarness.Skyrim, 2);
        _harness.Lookup.Pages[mods[1].Mod] = BrowseHarness.Page(mods[1]);

        await Screen.Results[1].OpenCommand.ExecuteAsync(null);

        Assert.Equal("Mod 2", Screen.Mod?.Name);
        Assert.Contains("LookUpAsync 2", _harness.Lookup.Asked);
    }

    [Fact]
    public async Task AModGoneFromItsSiteSaysSo()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 1);

        await Screen.Results[0].OpenCommand.ExecuteAsync(null);

        Assert.Equal("The site has no such mod.", Screen.Problem);
        Assert.Null(Screen.Mod);
    }

    [Fact]
    public async Task ANewSearchClosesTheModOpened()
    {
        List<ModInfo> mods = await ChooseAsync(BrowseHarness.Skyrim, 1);
        _harness.Lookup.Pages[mods[0].Mod] = BrowseHarness.Page(mods[0]);
        await Screen.Results[0].OpenCommand.ExecuteAsync(null);

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.Null(Screen.Mod);
    }

    [Fact]
    public async Task ListsTheChosenGamesTrackedModsTheHighestIdsFirst()
    {
        ModRef skyUi = TestData.Mod("12604");
        ModRef address = TestData.Mod("32444");
        _harness.Watchlists.Tracked.UnionWith([skyUi, address, TestData.Mod("2400", "stardewvalley"), TestData.Mod("266")]);
        _harness.Catalog.Mods["12604"] = BrowseHarness.Info(skyUi, "SkyUI");
        _harness.Catalog.Mods["32444"] = BrowseHarness.Info(address, "Address Library");
        await ChooseAsync(BrowseHarness.Skyrim, 2);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.True(Screen.IsShowingTracked);
        Assert.Equal("You track 3 mods of Skyrim Special Edition on Nexus Mods.", Screen.ResultsText);
        Assert.Equal(["Address Library", "SkyUI", "Mod 266"], _harness.Names());
        Assert.Equal([null, null, "Couldn't read its details: No Mod Found"], Screen.Results.Select(row => row.Problem));
        Assert.False(Screen.HasMore);
    }

    [Fact]
    public async Task TrackedModsAreListedAPageAtATime()
    {
        _harness.Watchlists.Tracked.UnionWith(Enumerable.Range(1, 25).Select(id => TestData.Mod($"{id}")));
        foreach (int id in Enumerable.Range(1, 25))
        {
            _harness.Catalog.Mods[$"{id}"] = BrowseHarness.Info(TestData.Mod($"{id}"), $"Tracked {id}");
        }

        await ChooseAsync(BrowseHarness.Skyrim, 0);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);
        (int first, bool more) = (Screen.Results.Count, Screen.HasMore);
        await Screen.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal((20, true), (first, more));
        Assert.Equal(Enumerable.Range(1, 25).Reverse().Select(id => $"Tracked {id}"), _harness.Names());
        Assert.False(Screen.HasMore);
        Assert.Single(_harness.Watchlists.Asked);
    }

    [Fact]
    public async Task NoTrackedModsSaysSo()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 0);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal("You track no mods of Skyrim Special Edition on Nexus Mods.", Screen.ResultsText);
        Assert.Empty(Screen.Results);
    }

    [Fact]
    public async Task TrackedModsTheSiteWontListSayWhy()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 1);
        _harness.Watchlists.Refusals["GetWatchedAsync"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods couldn't list your tracked mods: Service unavailable.", Screen.Problem);
        Assert.False(Screen.IsShowingTracked);
        Assert.Equal(["Mod 1"], _harness.Names());
    }

    [Fact]
    public async Task TrackedModsNeedAGameOfASiteThatTracksThem()
    {
        _harness.Watchlists.Tracking.Clear();
        await _harness.StartAsync();
        bool withoutAGame = Screen.ShowTrackedCommand.CanExecute(null);
        await _harness.ShowAsync();

        Screen.Game = Screen.Games[0];
        await Screen.Listing;

        Assert.False(withoutAGame);
        Assert.False(Screen.ShowTrackedCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchingAfterTheTrackedModsListsTheSearchAgain()
    {
        await ChooseAsync(BrowseHarness.Skyrim, 1);
        await Screen.ShowTrackedCommand.ExecuteAsync(null);

        await Screen.SearchCommand.ExecuteAsync(null);

        Assert.False(Screen.IsShowingTracked);
        Assert.Equal(["Mod 1"], _harness.Names());
    }

    [Fact]
    public async Task EachModShowsWhatTheLibraryHasOfItAndFollowsItsChanges()
    {
        ModRef first = new(BrowseHarness.Skyrim.Game, "1");
        ModRef second = new(BrowseHarness.Skyrim.Game, "2");
        _harness.Searchable(BrowseHarness.Skyrim, 2);
        await _harness.StartAsync(TestData.LibraryMod(first, "Mod 1", watched: false, TestData.LibraryFile(first, "10", "a.7z")));
        await _harness.ShowAsync();
        (bool, bool)[] before = [.. Screen.Results.Select(row => (row.IsDownloaded, row.IsWatched))];

        _harness.Library.Change(second, TestData.LibraryMod(second, "Mod 2"));
        _harness.Library.Change(first, null);

        Assert.Equal([(true, false), (false, false)], before);
        Assert.Equal([(false, false), (false, true)], Screen.Results.Select(row => (row.IsDownloaded, row.IsWatched)));
    }

    [Fact]
    public async Task EachModSaysWhatItsSiteCounts()
    {
        using CultureScope culture = new("en-US");

        // Far enough east that the mod's last update, at noon UTC, falls on the next day.
        _harness.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14"));
        ModRef mod = new(BrowseHarness.Skyrim.Game, "1");
        _harness.Lookup.Mods[BrowseHarness.Skyrim.Game] =
        [
            BrowseHarness.Info(mod, "SkyUI"),
            new ModInfo(new ModRef(BrowseHarness.Skyrim.Game, "2"), "Bare", isAvailable: false, hasAdultContent: true, endorsements: 5),
        ];
        await ChooseAsync(BrowseHarness.Skyrim, -1);

        BrowseModViewModel full = Screen.Results[0];
        BrowseModViewModel bare = Screen.Results[1];

        Assert.Equal(
            ("by An Author", "All about SkyUI.", "Version 1.2", "Updated 10/25/2026", "2,134,567 downloads", "1 endorsement", false, false),
            (full.AuthorText, full.Summary, full.VersionText, full.UpdatedText, full.DownloadsText, full.EndorsementsText, full.HasAdultContent, full.IsHidden));
        Assert.Equal(
            ("", "", "", "", "", "5 endorsements", true, true),
            (bare.AuthorText, bare.Summary, bare.VersionText, bare.UpdatedText, bare.DownloadsText, bare.EndorsementsText, bare.HasAdultContent, bare.IsHidden));
    }

    [Fact]
    public async Task TheWorkEndsWhenTheAppCloses()
    {
        await _harness.StartAsync();

        _harness.Dispose();

        await _harness.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Starts the screen, shows it, and chooses the game, which <paramref name="mods"/> mods are found for; or, when
    /// negative, the mods the test set up already.
    /// </summary>
    private async Task<List<ModInfo>> ChooseAsync(GameInfo game, int mods)
    {
        List<ModInfo> found = mods < 0 ? _harness.Lookup.Mods[game.Game] : _harness.Searchable(game, mods);
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.Game = Assert.Single(Screen.Games, choice => choice.Game == game.Game);
        await Screen.Listing;
        return found;
    }
}
