using ModShelf.Domain.Library;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library's filters find games and mods by the starts of their names' words, in any order, case, accents and
/// punctuation aside, and keep the order chosen.
/// </summary>
public sealed class LibraryNameFilterTests : IDisposable
{
    private static readonly LibraryMod Anomaly = TestData.LibraryMod(TestData.Mod("1", "stalker2heartofchornobyl"), "Anomaly Fix");
    private static readonly LibraryMod Gamma = TestData.LibraryMod(TestData.Mod("2", "stalker2heartofchornobyl"), "Gamma");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");
    private static readonly LibraryMod Ussep = TestData.LibraryMod(TestData.Mod("266"), "Unofficial Skyrim Patch");
    private static readonly LibraryMod Pokemon = TestData.LibraryMod(TestData.Mod("7"), "Pokémon Followers");

    private readonly LibraryHarness _harness = new();

    public LibraryNameFilterTests()
    {
        _harness.Catalog.GameNames["stalker2heartofchornobyl"] = "S.T.A.L.K.E.R. 2: Heart of Chornobyl";
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("stalker 2", "S.T.A.L.K.E.R. 2: Heart of Chornobyl")]
    [InlineData("heart chornobyl", "S.T.A.L.K.E.R. 2: Heart of Chornobyl")]
    [InlineData("valley stardew", "Stardew Valley")]
    [InlineData("edition skyrim", "Skyrim Special Edition")]
    public async Task TheGamesFilterFindsGamesByTheStartsOfTheirNamesWords(string filter, string game)
    {
        await StartAsync();

        Screen.GamesFilter = filter;

        Assert.Equal([game], Screen.Games.Select(shown => shown.Name));
    }

    [Fact]
    public async Task TheGamesFilterFindsNothingInsideAWord()
    {
        await StartAsync();

        Screen.GamesFilter = "dew";

        Assert.Equal((true, "No game's name has “dew”."), (Screen.NothingMatches, Screen.NothingMatchesText));
    }

    [Fact]
    public async Task TheGamesFilterKeepsTheOrderChosen()
    {
        _harness.Catalog.GameNames["valleylife"] = "Valley Life";
        await _harness.StartAsync(Smapi, TestData.LibraryMod(TestData.Mod("2401", "stardewvalley"), "Content Patcher"), TestData.LibraryMod(TestData.Mod("1", "valleylife"), "Farm"));
        Screen.SelectedGameOrder = Screen.GameOrders.Single(order => order.Value == GameOrder.MostMods);

        Screen.GamesFilter = "valley";

        // Stardew Valley has the most mods, so it comes first, though Valley Life starts with the word.
        Assert.Equal(["Stardew Valley", "Valley Life"], Screen.Games.Select(shown => shown.Name));
    }

    [Theory]
    [InlineData("patch unofficial", "Unofficial Skyrim Patch")]
    [InlineData("pokemon", "Pokémon Followers")]
    [InlineData("POKÉ FOLL", "Pokémon Followers")]
    public async Task TheModsFilterFindsModsByTheStartsOfTheirNamesWords(string filter, string mod)
    {
        await StartAsync();
        _harness.Game(TestData.Mod("266").Game).OpenCommand.Execute(null);

        Screen.ModsFilter = filter;

        Assert.Equal([mod], Screen.GameMods.Select(shown => shown.Name));
    }

    [Fact]
    public async Task TheModsFilterFindsNothingInsideAWord()
    {
        await StartAsync();
        _harness.Game(TestData.Mod("266").Game).OpenCommand.Execute(null);

        Screen.ModsFilter = "ficial";

        Assert.True(Screen.NoModMatches);
    }

    private Task StartAsync() => _harness.StartAsync(Anomaly, Gamma, Smapi, Ussep, Pokemon);
}
