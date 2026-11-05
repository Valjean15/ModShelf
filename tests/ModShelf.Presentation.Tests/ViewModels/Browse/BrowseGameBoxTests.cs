using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// The game picker finds games by the starts of their names' words, in any order, those that start with what's typed
/// first. The library has none of these games.
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
    public async Task SaysHowManyGamesItFindsWhileNothingsTyped()
    {
        await StartAsync();

        Assert.Empty(Screen.Picker.Entries);
        Assert.Equal("Type to find any of the 6 games on Nexus Mods.", Screen.Picker.Hint);
        Assert.Equal(6, Screen.Games.Count);
    }

    [Theory]
    [InlineData("stalker 2", "S.T.A.L.K.E.R. 2: Heart of Chornobyl")]
    [InlineData("final fantasy 12", "Final Fantasy XII: The Zodiac Age")]
    [InlineData("dragons dogma", "Dragon's Dogma 2")]
    [InlineData("zodiac", "Final Fantasy XII: The Zodiac Age")]
    public async Task FindsAGameByTheStartsOfItsNamesWords(string typed, string game)
    {
        await StartAsync();

        Screen.Picker.Query = typed;

        Assert.Equal([game], Names());
    }

    [Fact]
    public async Task ListsTheGamesThatStartWithItFirst()
    {
        await StartAsync();

        Screen.Picker.Query = "valley";

        Assert.Equal(["Valley Life", "Stardew Valley"], Names());
    }

    [Fact]
    public async Task FindsNoGameInsideAWord()
    {
        await StartAsync();

        Screen.Picker.Query = "dew";

        Assert.Empty(Screen.Picker.Entries);
        Assert.Equal("No game's name has “dew”.", Screen.Picker.NothingFound);
    }

    [Fact]
    public async Task TheHintAgainOnceTheTextIsCleared()
    {
        await StartAsync();
        Screen.Picker.Query = "fallout";

        Screen.Picker.Query = string.Empty;

        Assert.Empty(Screen.Picker.Entries);
        Assert.NotNull(Screen.Picker.Hint);
        Assert.Null(Screen.Picker.NothingFound);
    }

    private static GameInfo Game(string key, string name) => new(new GameRef(TestData.Nexus, key), name);

    private string[] Names() => [.. Screen.Picker.Entries.Select(entry => entry.Name)];

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
    }
}
