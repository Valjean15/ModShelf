using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// The game box finds games by the starts of their names' words, in any order, those that start with what's typed
/// first.
/// </summary>
public sealed class BrowseGameBoxTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    public BrowseGameBoxTests()
    {
        _harness.Lookup.Games[TestData.Nexus] =
        [
            Game("stardewvalley", "Stardew Valley"),
            Game("valleylife", "Valley Life"),
            Game("stalker2heartofchornobyl", "S.T.A.L.K.E.R. 2: Heart of Chornobyl"),
            Game("finalfantasy12", "Final Fantasy XII: The Zodiac Age"),
            Game("dragonsdogma2", "Dragon's Dogma 2"),
            Game("fallout4", "Fallout 4"),
        ];
    }

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsEveryGameByNameWhileNothingsTyped()
    {
        await StartAsync();

        Assert.Equal(Screen.Games, Screen.GameMatches);
        Assert.Equal(6, Screen.GameMatches.Count);
    }

    [Theory]
    [InlineData("stalker 2", "S.T.A.L.K.E.R. 2: Heart of Chornobyl")]
    [InlineData("final fantasy 12", "Final Fantasy XII: The Zodiac Age")]
    [InlineData("dragons dogma", "Dragon's Dogma 2")]
    [InlineData("zodiac", "Final Fantasy XII: The Zodiac Age")]
    public async Task FindsAGameByTheStartsOfItsNamesWords(string typed, string game)
    {
        await StartAsync();

        Screen.GameText = typed;

        Assert.Equal([game], Screen.GameMatches.Select(match => match.Name));
    }

    [Fact]
    public async Task ListsTheGamesThatStartWithItFirst()
    {
        await StartAsync();

        Screen.GameText = "valley";

        Assert.Equal(["Valley Life", "Stardew Valley"], Screen.GameMatches.Select(match => match.Name));
    }

    [Fact]
    public async Task FindsNoGameInsideAWord()
    {
        await StartAsync();

        Screen.GameText = "dew";

        Assert.Empty(Screen.GameMatches);
    }

    [Fact]
    public async Task EveryGameAgainOnceTheTextIsCleared()
    {
        await StartAsync();
        Screen.GameText = "fallout";

        Screen.GameText = string.Empty;

        Assert.Equal(Screen.Games, Screen.GameMatches);
    }

    private static GameInfo Game(string key, string name) => new(new GameRef(TestData.Nexus, key), name);

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
    }
}
