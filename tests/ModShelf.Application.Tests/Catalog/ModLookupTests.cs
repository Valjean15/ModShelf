using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Application.Tests.Catalog;

public sealed class ModLookupTests
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsksTheLinksSourceAboutItsMod()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await LookUpAsync(site);

        Assert.Equal(site.Game, page.Game);
        Assert.Equal(site.Mod, page.Mod);
        Assert.Equal(site.Files.Select(file => file.Reference), page.Files.Select(file => file.Reference));
        Assert.Equal(site.Changelogs, page.Changelogs);
        Assert.Equal(site.LinkedFile, page.LinkedFile);
        Assert.Equal(
            ["GetChangelogsAsync", "GetFilesAsync", "GetGameAsync", "GetModAsync", "RecognizeAsync"],
            site.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TellsWhatChangedInAVersionReadingEachChangelogOnce()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        ModLookup lookup = new(new SourceRegistry([site]));
        ModRef mod = site.LinkedFile.Mod;

        IReadOnlyList<string>? changed = await lookup.GetChangesAsync(mod, "5.2", Cancellation);
        IReadOnlyList<string>? unlisted = await lookup.GetChangesAsync(mod, "5.3", Cancellation);
        IReadOnlyList<string>? otherCase = await lookup.GetChangesAsync(mod, "5.2 ", Cancellation);

        Assert.Equal(["Fixed the menus."], changed);
        Assert.Empty(unlisted!);
        Assert.Empty(otherCase!);
        Assert.Equal(["GetChangelogsAsync"], site.Asked);
    }

    [Fact]
    public async Task AVersionIsMatchedExactlyAsTheChangelogWritesIt()
    {
        FakeCatalogSource site = new(TestData.Nexus) { Changelogs = [new("1.0b", ["Fixed the menus."])] };
        ModLookup lookup = new(new SourceRegistry([site]));
        ModRef mod = site.LinkedFile.Mod;

        Assert.Empty((await lookup.GetChangesAsync(mod, "1.0B", Cancellation))!);
        Assert.Equal(["Fixed the menus."], await lookup.GetChangesAsync(mod, "1.0b", Cancellation));
    }

    [Fact]
    public async Task OneCallerGivingUpDoesntStopTheReadForTheOthers()
    {
        FakeCatalogSource site = new(TestData.Nexus) { ChangelogsHeld = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        ModLookup lookup = new(new SourceRegistry([site]));
        ModRef mod = site.LinkedFile.Mod;
        using CancellationTokenSource givingUp = new();

        Task<IReadOnlyList<string>?> first = lookup.GetChangesAsync(mod, "5.2", givingUp.Token);
        Task<IReadOnlyList<string>?> second = lookup.GetChangesAsync(mod, "5.2", Cancellation);
        await givingUp.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        site.ChangelogsHeld.SetResult();

        Assert.Equal(["Fixed the menus."], await second.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
        Assert.Equal(["GetChangelogsAsync"], site.Asked);
    }

    [Fact]
    public async Task AChangelogThatCouldntBeReadIsReadAgain()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        site.Failures["GetChangelogsAsync"] = new HttpRequestException("The site didn't answer.");
        ModLookup lookup = new(new SourceRegistry([site]));

        await Assert.ThrowsAsync<HttpRequestException>(() => lookup.GetChangesAsync(site.LinkedFile.Mod, "5.2", Cancellation));
        IReadOnlyList<string>? changed = await lookup.GetChangesAsync(site.LinkedFile.Mod, "5.2", Cancellation);

        Assert.Equal(["Fixed the menus."], changed);
        Assert.Equal(["GetChangelogsAsync", "GetChangelogsAsync"], site.Asked);
    }

    [Fact]
    public async Task AModOfASiteWithoutCatalogHasNoChanges()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        Assert.Null(await lookup.GetChangesAsync(TestData.File(TestData.Nexus, "35407").Mod, "5.2", Cancellation));
        Assert.Null(await lookup.GetChangesAsync(TestData.File(TestData.ModIo, "1").Mod, "5.2", Cancellation));
    }

    [Fact]
    public async Task ASiteSpecificMatchWinsOverACatchAll()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        ScriptedLinkRecognizer catchAll = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        ModPage page = await LookUpAsync(catchAll, site);

        Assert.Equal(site.LinkedFile, page.LinkedFile);
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => LookUpAsync(ScriptedLinkRecognizer.Never(TestData.Nexus)));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task SaysWhenTheLinksSourceHasNoCatalog()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => LookUpAsync(direct));

        Assert.Contains("isn't to a mod", refused.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "GetGameAsync", new AuthenticationFailedException("The site refused the account.") },
        { "GetModAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
        { "GetFilesAsync", new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable) },
        { "GetChangelogsAsync", new HttpRequestException("No route to host.") },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSourceRefuses(string method, Exception failure)
    {
        FakeCatalogSource site = new(TestData.Nexus);
        site.Failures[method] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => LookUpAsync(site));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task SearchesThroughTheGamesSource()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        GameRef game = TestData.File(TestData.Nexus, "1").Mod.Game;
        SearchQuery query = new("SkyUI", SearchSort.Downloads, offset: 20);

        SearchPage page = await new ModLookup(new SourceRegistry([site, new FakeCatalogSource(TestData.ModIo)]))
            .SearchAsync(game, query, TestContext.Current.CancellationToken);

        Assert.Equal(site.Mod, Assert.Single(page.Mods));
        Assert.Equal((game, query), site.LastSearch);
    }

    [Fact]
    public async Task LeavesOutTheAdultModsASiteMarksButCantLeaveOut()
    {
        FakeCatalogSource site = AdultSite(marks: true, filters: false);

        SearchPage page = await SearchAdultSiteAsync(site, includeAdult: false);

        Assert.Equal(["Plain 1", "Plain 2"], page.Mods.Select(mod => mod.Name));
        Assert.Equal((20, 2, 24, true), (page.Offset, page.LeftOut, page.NextOffset, page.HasMore));
        Assert.Equal(30, page.TotalCount);
        Assert.False(site.LastSearch?.Query.IncludeAdultContent);
    }

    [Fact]
    public async Task CountsTheModsASiteLeftOutItselfWithTheAdultOnes()
    {
        FakeCatalogSource site = AdultSite(marks: true, filters: false);
        site.Found = new SearchPage(site.Found!.Mods, site.Found.TotalCount, site.Found.Offset, leftOut: 3);

        SearchPage page = await SearchAdultSiteAsync(site, includeAdult: false);

        Assert.Equal((5, 27), (page.LeftOut, page.NextOffset));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task KeepsTheAdultModsWhenAskedOrWhenTheSiteChoseItself(bool marks, bool filters, bool includeAdult)
    {
        FakeCatalogSource site = AdultSite(marks, filters);

        SearchPage page = await SearchAdultSiteAsync(site, includeAdult);

        Assert.Same(site.Found, page);
    }

    [Fact]
    public async Task RefusesToSearchWithASourceThatCant()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.SearchAsync(
            TestData.File(TestData.Nexus, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.SearchAsync(
            TestData.File(TestData.ModIo, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PassesOnWhatTheSearchRefuses()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("Field 'nameStemmed' is not defined.");
        site.Failures["SearchAsync"] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => new ModLookup(new SourceRegistry([site]))
            .SearchAsync(TestData.File(TestData.Nexus, "1").Mod.Game, new SearchQuery(), TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task LooksAModUpByItsReferenceWithoutALinkedFile()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await new ModLookup(new SourceRegistry([site])).LookUpAsync(site.Mod.Mod, TestContext.Current.CancellationToken);

        Assert.Equal((site.Game, site.Mod, site.Changelogs), (page.Game, page.Mod, page.Changelogs));
        Assert.Equal(site.Files.Select(file => file.Reference), page.Files.Select(file => file.Reference));
        Assert.Null(page.LinkedFile);
        Assert.Equal(["GetChangelogsAsync", "GetFilesAsync", "GetGameAsync", "GetModAsync"], site.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AModPageHasWhatItsSourceSaysItNeeds()
    {
        FakeCatalogSource catalog = new(TestData.Nexus);
        ModRequirement[] needs =
        [
            new("SMAPI", new ModRef(new GameRef(TestData.Nexus, "stardewvalley"), "2400")),
            new("Script Extender", pageUrl: new Uri("https://example.com/extender")),
        ];
        CatalogWithRequirements site = new(catalog) { Requirements = needs };

        ModPage page = await new ModLookup(new SourceRegistry([site])).LookUpAsync(catalog.Mod.Mod, TestContext.Current.CancellationToken);

        Assert.Equal(needs, page.Requirements);
        Assert.Equal([catalog.Mod.Mod], site.Asked);
    }

    [Fact]
    public async Task AModPageOfASourceThatCantSayWhatModsNeedListsNothing()
    {
        FakeCatalogSource site = new(TestData.Nexus);

        ModPage page = await new ModLookup(new SourceRegistry([site])).LookUpAsync(site.Mod.Mod, TestContext.Current.CancellationToken);

        Assert.Empty(page.Requirements);
    }

    [Fact]
    public async Task AModPageIsntShownWhenItsRequirementsCantBeRead()
    {
        HttpRequestException failure = new("Service unavailable.");
        FakeCatalogSource catalog = new(TestData.Nexus);
        CatalogWithRequirements site = new(catalog) { Failure = failure };

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => new ModLookup(new SourceRegistry([site])).LookUpAsync(catalog.Mod.Mod, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task RefusesToLookUpAModByReferenceWhoseSourceCantDescribeIt()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.LookUpAsync(TestData.File(TestData.Nexus, "1").Mod, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.LookUpAsync(TestData.File(TestData.ModIo, "1").Mod, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LookingUpByReferencePassesOnWhatTheSourceRefuses()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("No Mod Found", null, HttpStatusCode.NotFound);
        site.Failures["GetModAsync"] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => new ModLookup(new SourceRegistry([site])).LookUpAsync(site.Mod.Mod, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task ReadsEachSourcesGamesOnceAndKeepsThem()
    {
        FakeCatalogSource nexus = new(TestData.Nexus);
        FakeCatalogSource modIo = new(TestData.ModIo);
        ModLookup lookup = new(new SourceRegistry([nexus, modIo]));

        IReadOnlyList<GameInfo> first = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> again = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> other = await lookup.GetGamesAsync(TestData.ModIo, TestContext.Current.CancellationToken);

        Assert.Equal([nexus.Game], first);
        Assert.Same(first, again);
        Assert.Equal([modIo.Game], other);
        Assert.Equal(["GetGamesAsync"], nexus.Asked);
        Assert.Equal(["GetGamesAsync"], modIo.Asked);
    }

    [Fact]
    public async Task ReadsTheGamesAgainAfterAFailedRead()
    {
        FakeCatalogSource site = new(TestData.Nexus);
        HttpRequestException failure = new("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        site.Failures["GetGamesAsync"] = failure;
        ModLookup lookup = new(new SourceRegistry([site]));

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken));
        IReadOnlyList<GameInfo> games = await lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken);

        Assert.Same(failure, thrown);
        Assert.Equal([site.Game], games);
        Assert.Equal(["GetGamesAsync", "GetGamesAsync"], site.Asked);
    }

    [Fact]
    public async Task FindsTheGamesOfASiteThatListsThemInItsWholeList()
    {
        FakeCatalogSource site = new(TestData.Nexus) { Games = [Named("Stardew Valley"), Named("Valheim"), Named("Fallout 4"), Named("Valley of Dragons")] };
        ModLookup lookup = new(new SourceRegistry([site]));

        IReadOnlyList<GameInfo> valley = await lookup.FindGamesAsync(TestData.Nexus, NameQuery.Parse("valley"), TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> every = await lookup.FindGamesAsync(TestData.Nexus, NameQuery.Every, TestContext.Current.CancellationToken);

        Assert.Equal(["Valley of Dragons", "Stardew Valley"], valley.Select(game => game.Name));
        Assert.Equal(["Fallout 4", "Stardew Valley", "Valheim", "Valley of Dragons"], every.Select(game => game.Name));
        Assert.Equal(["GetGamesAsync"], site.Asked);
        Assert.False(lookup.FindsGamesByName(TestData.Nexus));
    }

    [Fact]
    public async Task AsksASiteThatFindsItsGamesByNameEachTimeAndMatchesWhatItFindsAsTheAppDoes()
    {
        SourceId banana = new("banana");
        GameRef persona = new(banana, "16951");
        GameSearchSite site = new(new FakeCatalogSource(banana))
        {
            Found =
            [
                new GameInfo(persona, "Persona 5 Royal (PC)"),
                new GameInfo(new GameRef(banana, "7545"), "Persona 5"),
                new GameInfo(new GameRef(banana, "8502"), "Persona 3 FES"),
                new GameInfo(persona, "Persona 5 Royal (PC)"),
                new GameInfo(new GameRef(TestData.Nexus, "persona5"), "Persona 5"),
            ],
        };
        ModLookup lookup = new(new SourceRegistry([site]));

        IReadOnlyList<GameInfo> found = await lookup.FindGamesAsync(banana, NameQuery.Parse("persona 5"), TestContext.Current.CancellationToken);
        IReadOnlyList<GameInfo> again = await lookup.FindGamesAsync(banana, NameQuery.Parse(" Persona 5 "), TestContext.Current.CancellationToken);

        Assert.True(lookup.FindsGamesByName(banana));
        Assert.Equal([new GameRef(banana, "7545"), persona], found.Select(game => game.Game));
        Assert.Equal(found, again);
        Assert.Equal(["persona 5", "Persona 5"], site.Asked);
    }

    [Fact]
    public void ASiteThatFindsGamesByNameButCantDescribeThemDoesntFindThem()
    {
        SourceId banana = new("banana");
        ModLookup lookup = new(new SourceRegistry([new SearchOnlySite(banana)]));

        Assert.False(lookup.FindsGamesByName(banana));
        Assert.False(lookup.FindsGamesByName(new SourceId("nowhere")));
    }

    [Fact]
    public async Task RefusesToListEveryGameOfASiteThatFindsThemByName()
    {
        SourceId banana = new("banana");
        GameSearchSite site = new(new FakeCatalogSource(banana));
        ModLookup lookup = new(new SourceRegistry([site]));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.FindGamesAsync(banana, NameQuery.Parse(" .. "), TestContext.Current.CancellationToken));

        Assert.StartsWith("banana has too many games to list them all: give words of the game's name.", refused.Message, StringComparison.Ordinal);
        Assert.Empty(site.Asked);
    }

    [Fact]
    public async Task PassesOnASearchTheSiteCantDo()
    {
        SourceId banana = new("banana");
        ArgumentException tooShort = new("A word of 3 letters or more.");
        GameSearchSite site = new(new FakeCatalogSource(banana)) { Failure = tooShort };
        ModLookup lookup = new(new SourceRegistry([site]));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.FindGamesAsync(banana, NameQuery.Parse("p5"), TestContext.Current.CancellationToken));

        Assert.Same(tooShort, refused);
    }

    [Fact]
    public async Task RefusesTheGamesOfASourceThatCantDescribeThem()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.GetGamesAsync(TestData.Nexus, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.GetGamesAsync(TestData.ModIo, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ListsTheSourcesThatCanSearch()
    {
        ModLookup lookup = new(new SourceRegistry(
            [
                new FakeCatalogSource(TestData.Nexus),
                ScriptedLinkRecognizer.Never(new SourceId("direct")),
                new CatalogOnlySource(new SourceId("catalog-only")),
                new FakeCatalogSource(TestData.ModIo),
            ]));

        Assert.Equal([TestData.Nexus, TestData.ModIo], lookup.SearchableSources);
    }

    [Fact]
    public async Task SaysWhatTheModsSourceCantDo()
    {
        ModLookup lookup = new(new SourceRegistry([ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => lookup.LookUpAsync(TestData.File(TestData.Nexus, "35407").Mod, TestContext.Current.CancellationToken));

        Assert.Equal($"Source '{TestData.Nexus}' isn't registered, or can't describe its mods. (Parameter 'modRef')", refused.Message);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        ModLookup lookup = new(new SourceRegistry([new FakeCatalogSource(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookUpAsync(
            new Uri("skyrimspecialedition/mods/12604", UriKind.Relative), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A site whose search finds a page of four mods, 21 to 24 of 30, two with adult content; it marks them, or can leave
    /// them out itself, as told.
    /// </summary>
    private static FakeCatalogSource AdultSite(bool marks, bool filters)
    {
        FakeCatalogSource site = new(TestData.Nexus)
        {
            Capabilities = new(
                [new SearchOrder(SearchSort.Relevance, SearchDirection.Descending)],
                filters ? SearchFilters.AdultContent : SearchFilters.None,
                SearchQuery.MaxCount,
                marks),
        };
        GameRef game = site.LinkedFile.Mod.Game;
        site.Found = new SearchPage(
            [
                new ModInfo(new ModRef(game, "1"), "Plain 1"),
                new ModInfo(new ModRef(game, "2"), "Adult 1", hasAdultContent: true),
                new ModInfo(new ModRef(game, "3"), "Plain 2"),
                new ModInfo(new ModRef(game, "4"), "Adult 2", hasAdultContent: true),
            ],
            30,
            20);
        return site;
    }

    private static Task<SearchPage> SearchAdultSiteAsync(FakeCatalogSource site, bool includeAdult)
        => new ModLookup(new SourceRegistry([site])).SearchAsync(
            site.LinkedFile.Mod.Game, new SearchQuery(offset: 20, count: 4, includeAdultContent: includeAdult), TestContext.Current.CancellationToken);

    private static Task<ModPage> LookUpAsync(params IModSource[] sources)
        => new ModLookup(new SourceRegistry(sources)).LookUpAsync(Link, TestContext.Current.CancellationToken);

    /// <summary>
    /// A catalog whose source also says what its mods need, recording which mods it was asked about.
    /// </summary>
    private sealed class CatalogWithRequirements(FakeCatalogSource catalog) : IModCatalog, IModRequirements
    {
        private readonly List<ModRef> _asked = [];

        public SourceId Id => catalog.Id;

        public string DisplayName => catalog.DisplayName;


        public SiteWords Words => catalog.Words;

        public IReadOnlyList<ModRequirement> Requirements { get; init; } = [];

        /// <summary>
        /// What reading the requirements throws instead, when set.
        /// </summary>
        public Exception? Failure { get; init; }

        public IReadOnlyList<ModRef> Asked => _asked;

        public Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
        {
            _asked.Add(modRef);
            return Failure is { } failure ? Task.FromException<IReadOnlyList<ModRequirement>>(failure) : Task.FromResult(Requirements);
        }

        public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => catalog.GetGamesAsync(cancellationToken);

        public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => catalog.GetGameAsync(game, cancellationToken);

        public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => catalog.GetModAsync(modRef, cancellationToken);

        public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => catalog.GetFilesAsync(modRef, cancellationToken);

        public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
            => catalog.GetChangelogsAsync(modRef, cancellationToken);
    }

    private static GameInfo Named(string name) => new(new GameRef(TestData.Nexus, name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()), name);

    /// <summary>
    /// A site that finds its games by name, finding <see cref="Found"/> whatever is typed, or failing as told.
    /// </summary>
    /// <summary>
    /// A site with a search of its games by name, and no catalog to describe them.
    /// </summary>
    private sealed class SearchOnlySite(SourceId id) : IGameSearch
    {
        public SourceId Id => id;

        public string DisplayName => "Search only";

        public SiteWords Words => SiteWords.None;

        public Task<IReadOnlyList<GameInfo>> FindGamesAsync(string words, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GameInfo>>([]);
    }

    private sealed class GameSearchSite(FakeCatalogSource catalog) : IModCatalog, IGameSearch
    {
        private readonly List<string> _asked = [];

        public SourceId Id => catalog.Id;

        public string DisplayName => catalog.DisplayName;

        public SiteWords Words => catalog.Words;

        public IReadOnlyList<GameInfo> Found { get; init; } = [];

        public Exception? Failure { get; init; }

        /// <summary>
        /// The words searched for, in order.
        /// </summary>
        public IReadOnlyList<string> Asked => _asked;

        public Task<IReadOnlyList<GameInfo>> FindGamesAsync(string words, CancellationToken cancellationToken)
        {
            _asked.Add(words);
            return Failure is { } failure ? Task.FromException<IReadOnlyList<GameInfo>>(failure) : Task.FromResult(Found);
        }

        public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Its games are found by name.");

        public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => catalog.GetGameAsync(game, cancellationToken);

        public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => catalog.GetModAsync(modRef, cancellationToken);

        public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => catalog.GetFilesAsync(modRef, cancellationToken);

        public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
            => catalog.GetChangelogsAsync(modRef, cancellationToken);
    }
}
