using System.Collections.Immutable;

using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Installed;

/// <summary>
/// The installed games matched with the sites' games: by the same name, or as the user picked, kept in the settings.
/// </summary>
public sealed class InstalledGameMatcherTests
{
    private static readonly SourceId GameBanana = new("gamebanana");
    private static readonly GameInfo NexusSkyrim = Game(TestData.Nexus, "skyrimspecialedition", "Skyrim Special Edition");
    private static readonly GameInfo NexusStardew = Game(TestData.Nexus, "stardewvalley", "Stardew Valley");
    private static readonly GameInfo NexusNewVegas = Game(TestData.Nexus, "newvegas", "Fallout New Vegas");
    private static readonly GameInfo ModIoStardew = Game(TestData.ModIo, "stardew-valley", "Stardew Valley");

    private readonly ScriptedInstalledGames _installed = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeCatalogSource _gameBanana = new(GameBanana);
    private readonly RecordingLogger<InstalledGameMatcher> _logger = new();
    private readonly InstalledGameMatcher _matcher;
    private readonly InstalledGame _skyrim;
    private readonly InstalledGame _stardew;
    private readonly InstalledGame _newVegas;

    public InstalledGameMatcherTests()
    {
        _skyrim = _installed.Add("489830", "The Elder Scrolls V: Skyrim Special Edition");
        _stardew = _installed.Add("413150", "Stardew Valley");
        _newVegas = _installed.Add("22380", "Fallout: New Vegas");
        SettingsService settings = new(_store, new FakeUserFolders(), new InMemoryLibraryRepository(), new InMemoryDownloadJobRepository([]));
        _matcher = new InstalledGameMatcher(_installed, settings, new SourceRegistry(new IModSource[] { _gameBanana }), _logger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MatchesEachInstalledGameOnEachListedSiteByTheSameName()
    {
        InstalledGamesMatched matched = await _matcher.MatchAsync([NexusSkyrim, NexusStardew, NexusNewVegas, ModIoStardew], Cancellation);

        Assert.Equal([ScriptedInstalledGames.Steam], matched.Stores);
        Assert.Equal(
            [
                "The Elder Scrolls V: Skyrim Special Edition | nexus: - | mod-io: -",
                "Stardew Valley | nexus: Stardew Valley | mod-io: Stardew Valley",
                "Fallout: New Vegas | nexus: Fallout New Vegas | mod-io: -",
            ],
            Lines(matched));
        Assert.All(matched.Games.SelectMany(game => game.Sites), site => Assert.False(site.Picked));
        Assert.Same(_stardew, matched.Games[1].Game);
    }

    [Fact]
    public async Task ANameTwoGamesOfASiteShareMatchesBoth()
    {
        GameInfo other = Game(TestData.Nexus, "stardewvalleyexpanded", "Stardew Valley");

        InstalledGamesMatched matched = await _matcher.MatchAsync([NexusStardew, other], Cancellation);

        Assert.Equal([NexusStardew, other], matched.Games[1].On(TestData.Nexus)?.Games);
    }

    [Fact]
    public async Task GamesListedTwiceCountOnce()
    {
        InstalledGamesMatched matched = await _matcher.MatchAsync([NexusStardew, NexusStardew with { }], Cancellation);

        Assert.Equal([NexusStardew], matched.Games[1].On(TestData.Nexus)?.Games);
    }

    [Fact]
    public async Task WithNoGamesListedEachInstalledGameHasNoSite()
    {
        InstalledGamesMatched matched = await _matcher.MatchAsync([], Cancellation);

        Assert.Equal(3, matched.Games.Count);
        Assert.All(matched.Games, game => Assert.Empty(game.Sites));
        Assert.Null(matched.Games[0].On(TestData.Nexus));
    }

    [Fact]
    public async Task AGamePickedOrNonePickedTakesTheNamesPlace()
    {
        await _matcher.PickAsync(_skyrim, TestData.Nexus, new GameRef(TestData.Nexus, "SkyrimSpecialEdition"), Cancellation);
        await _matcher.PickAsync(_stardew, TestData.ModIo, null, Cancellation);

        InstalledGamesMatched matched = await _matcher.MatchAsync([NexusSkyrim, NexusStardew, ModIoStardew], Cancellation);

        Assert.Equal(
            [
                "The Elder Scrolls V: Skyrim Special Edition | nexus: Skyrim Special Edition (picked) | mod-io: -",
                "Stardew Valley | nexus: Stardew Valley | mod-io: - (picked)",
                "Fallout: New Vegas | nexus: - | mod-io: -",
            ],
            Lines(matched));
        Assert.Same(NexusSkyrim, matched.Games[0].On(TestData.Nexus)?.Games[0]);
    }

    [Fact]
    public async Task AGamePickedOnASiteNotListedIsAskedOfTheSiteOnceARunAndComesAfterTheListedSites()
    {
        await _matcher.PickAsync(_skyrim, GameBanana, _gameBanana.Game.Game, Cancellation);
        await _matcher.PickAsync(_skyrim, new SourceId("zzz"), null, Cancellation);
        await _matcher.PickAsync(_skyrim, new SourceId("aaa"), null, Cancellation);

        InstalledGamesMatched first = await _matcher.MatchAsync([NexusSkyrim], Cancellation);
        InstalledGamesMatched again = await _matcher.MatchAsync([NexusSkyrim], Cancellation);

        Assert.Equal(
            "The Elder Scrolls V: Skyrim Special Edition | nexus: - | aaa: - (picked) | gamebanana: Skyrim Special Edition (picked) | zzz: - (picked)",
            Lines(first)[0]);
        Assert.Equal(Lines(first), Lines(again));
        Assert.Equal(["GetGameAsync"], _gameBanana.Asked);
        Assert.Equal(2, _installed.Reads);
    }

    [Fact]
    public async Task AGamePickedThatItsSiteCantDescribeShowsItsKeyAndIsAskedAgainNextTime()
    {
        await _matcher.PickAsync(_skyrim, GameBanana, _gameBanana.Game.Game, Cancellation);
        _gameBanana.Failures["GetGameAsync"] = new HttpRequestException("GameBanana is down.");

        InstalledGamesMatched down = await _matcher.MatchAsync([], Cancellation);
        InstalledGamesMatched up = await _matcher.MatchAsync([], Cancellation);

        Assert.Equal("The Elder Scrolls V: Skyrim Special Edition | gamebanana: skyrimspecialedition (picked)", Lines(down)[0]);
        Assert.Equal("The Elder Scrolls V: Skyrim Special Edition | gamebanana: Skyrim Special Edition (picked)", Lines(up)[0]);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't ask gamebanana for a game picked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGamePickedOnASiteNoLongerOfferedShowsItsKey()
    {
        SourceId gone = new("gone");
        await _matcher.PickAsync(_stardew, gone, new GameRef(gone, "stardew"), Cancellation);

        InstalledGamesMatched matched = await _matcher.MatchAsync([], Cancellation);

        Assert.Equal("Stardew Valley | gone: stardew (picked)", Lines(matched)[1]);
    }

    [Fact]
    public async Task PicksAreKeptInTheSettingsAndForgottenOneByOne()
    {
        await _matcher.PickAsync(_skyrim, TestData.Nexus, NexusSkyrim.Game, Cancellation);
        await _matcher.PickAsync(_skyrim, TestData.ModIo, null, Cancellation);
        IReadOnlyDictionary<SourceId, string?> both = _store.Settings.InstalledMatches["steam/489830"];

        await _matcher.UnpickAsync(_skyrim, TestData.Nexus, Cancellation);
        IReadOnlyDictionary<SourceId, string?> one = _store.Settings.InstalledMatches["steam/489830"];
        await _matcher.UnpickAsync(_skyrim, TestData.ModIo, Cancellation);
        await _matcher.UnpickAsync(_newVegas, TestData.ModIo, Cancellation);

        Assert.Equal([(TestData.ModIo, (string?)null), (TestData.Nexus, "skyrimspecialedition")], both.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value)));
        Assert.Equal([(TestData.ModIo, (string?)null)], one.Select(entry => (entry.Key, entry.Value)));
        Assert.Empty(_store.Settings.InstalledMatches);
    }

    [Fact]
    public async Task PickingAnotherSitesGameIsRefused()
    {
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => _matcher.PickAsync(_skyrim, TestData.ModIo, NexusSkyrim.Game, Cancellation));

        Assert.Equal("picked", refused.ParamName);
        Assert.Empty(_store.Settings.InstalledMatches);
    }

    [Fact]
    public async Task SettingsThatCantBeReadMatchByNameAlone()
    {
        _store.Settings = new AppSettings
        {
            InstalledMatches = ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>>.Empty
                .Add("steam/413150", new Dictionary<SourceId, string?> { [TestData.Nexus] = null }),
        };
        _store.Failure = new InvalidDataException("The settings aren't JSON.");

        InstalledGamesMatched matched = await _matcher.MatchAsync([NexusStardew], Cancellation);

        Assert.Equal("Stardew Valley | nexus: Stardew Valley", Lines(matched)[1]);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read the settings", StringComparison.Ordinal));
    }

    private static GameInfo Game(SourceId site, string key, string name) => new(new GameRef(site, key), name);

    private static List<string> Lines(InstalledGamesMatched matched)
        => [.. matched.Games.Select(game => string.Join(
            " | ",
            [game.Game.Name, .. game.Sites.Select(site => $"{site.Site}: {(site.Games.Count == 0 ? "-" : string.Join(", ", site.Games.Select(found => found.Name)))}{(site.Picked ? " (picked)" : string.Empty)}")]))];
}
