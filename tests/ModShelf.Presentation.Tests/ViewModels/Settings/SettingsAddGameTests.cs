using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Settings' Add a game…: the game picker over the sites' games not listed yet, the one chosen added at once; a game's
/// key typed still adds one the sites can't list.
/// </summary>
public sealed class SettingsAddGameTests
{
    private static readonly GameInfo Skyrim = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "Skyrim Special Edition");
    private static readonly GameInfo Stardew = new(new GameRef(TestData.Nexus, "stardewvalley"), "Stardew Valley");
    private static readonly GameInfo Starfield = new(new GameRef(TestData.Nexus, "starfield"), "Starfield");

    private readonly SettingsHarness _harness = new();

    public SettingsAddGameTests()
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Lookup.Games[TestData.Nexus] = [Skyrim, Stardew, Starfield];
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
    }

    private SettingsViewModel Screen => _harness.Screen;

    [Fact]
    public async Task OffersTheSitesGamesNotListedAndSaysHowMany()
    {
        await StartAsync();
        AddGameViewModel? seen = null;
        _harness.Dialogs.InAddGame = async adding =>
        {
            await adding.Reading;
            seen = adding;
            adding.Picker.Query = "s";
            return false;
        };

        await Screen.AddGameCommand.ExecuteAsync(null);

        AddGameViewModel adding = Assert.IsType<AddGameViewModel>(seen);
        Assert.Equal(["Stardew Valley", "Starfield"], adding.Picker.Entries.Select(entry => entry.Name));
        Assert.Equal("Games on Nexus Mods", adding.Picker.Entries[0].Heading);
        Assert.Empty(adding.Picker.Recent);
        Assert.Equal((false, (string?)null), (adding.IsReading, adding.Problem));

        adding.Picker.Query = string.Empty;

        Assert.Equal("Type to find any of the 2 games on Nexus Mods.", adding.Picker.Hint);
        Assert.Equal(["GetGamesAsync nexus"], _harness.Lookup.Asked);
    }

    [Fact]
    public async Task TheGameChosenIsAddedAndShown()
    {
        await StartAsync();
        _harness.Dialogs.InAddGame = adding => PickAsync(adding, "starf");

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Equal(Starfield.Game, Assert.Single(_harness.Dialogs.AddingGames).Game);
        Assert.Contains(Screen.Games, row => row.Game == Starfield.Game);
        Assert.Equal(Starfield.Game, Screen.SelectedGame?.Game);
        Assert.Empty(_harness.Dialogs.ChoiceQuestions);
    }

    [Fact]
    public async Task AKeyTypedForNoGameListedIsAddedByItsKey()
    {
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue(" fallout4 ");

        await Screen.AddGameCommand.ExecuteAsync(null);

        AddGameViewModel adding = Assert.Single(_harness.Dialogs.AddingGames);
        Assert.Equal(((GameRef?)null, "fallout4"), (adding.Game, adding.TypedKey));
        Assert.Equal(new GameRef(TestData.Nexus, "fallout4"), Screen.SelectedGame?.Game);
    }

    [Fact]
    public async Task ASiteThatCantListItsGamesSaysSoAndAKeyStillAddsOne()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new HttpRequestException("Nexus Mods is down.");
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue("stardewvalley");

        await Screen.AddGameCommand.ExecuteAsync(null);

        AddGameViewModel adding = Assert.Single(_harness.Dialogs.AddingGames);
        Assert.Equal(
            "Nexus Mods couldn't list its games: Nexus Mods is down. Type a game's key to add it, such as skyrimspecialedition.",
            adding.Problem);
        Assert.Null(adding.Picker.Hint);
        Assert.Equal(Stardew.Game, Screen.SelectedGame?.Game);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLoggedAndSaidPlainly()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new InvalidOperationException("A bug.");
        await StartAsync();

        await Screen.AddGameCommand.ExecuteAsync(null);

        AddGameViewModel adding = Assert.Single(_harness.Dialogs.AddingGames);
        Assert.StartsWith("Something went wrong.", adding.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Type a game's key to add it, such as skyrimspecialedition.", adding.Problem, StringComparison.Ordinal);
        Assert.StartsWith("Error: Reading the games of nexus to add one failed System.InvalidOperationException: A bug.", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingItAddsNothing()
    {
        await StartAsync();
        int listed = Screen.Games.Count;

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.AddingGames);
        Assert.Equal(listed, Screen.Games.Count);
    }

    private static async Task<bool> PickAsync(AddGameViewModel adding, string typed)
    {
        await adding.Reading;
        bool added = false;
        adding.AddRequested += (_, _) => added = true;
        adding.Picker.Query = typed;
        GamePickerEntryViewModel entry = Assert.Single(adding.Picker.Entries);
        entry.PickCommand.Execute(null);
        return added;
    }

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
    }
}
