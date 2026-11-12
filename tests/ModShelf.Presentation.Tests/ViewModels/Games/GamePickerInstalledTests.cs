using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Games;

/// <summary>
/// The game picker's games on this PC: the installed games' games on the sites, first, under "On this PC (Steam)".
/// </summary>
public sealed class GamePickerInstalledTests
{
    private static readonly SourceId Banana = new("gamebanana");
    private static readonly GameInfo Stardew = new(Nexus("stardewvalley"), "Stardew Valley");
    private static readonly GameInfo Skyrim = new(Nexus("skyrimspecialedition"), "Skyrim Special Edition");
    private static readonly GameInfo Fallout = new(Nexus("fallout4"), "Fallout 4");
    private static readonly GameInfo Starfield = new(Nexus("starfield"), "Starfield");
    private static readonly GameInfo Royal = new(new GameRef(Banana, "16951"), "Persona 5 Royal (PC)");

    private readonly TestPictures _pictures = new();
    private readonly List<GameRef> _chosen = [];
    private readonly List<(InstalledGame Installed, GameRef Game)> _picked = [];
    private readonly GamePickerViewModel _picker;

    public GamePickerInstalledTests()
    {
        FakeSourceRegistry sources = new(new FakeSource(TestData.Nexus, "Nexus Mods"), new FakeSource(Banana, "GameBanana"));
        _picker = new GamePickerViewModel(_pictures.Pictures, sources, _chosen.Add, installedPicked: (installed, game) => _picked.Add((installed, game)));
        _picker.ShowGames([Fallout, Stardew, Skyrim, Starfield]);
    }

    [Fact]
    public void ListsTheInstalledGamesGamesFirstThenYourOthers()
    {
        _picker.ShowLibrary(Library((Stardew, 21), (Skyrim, 14)));

        _picker.ShowInstalled(
            [ScriptedInstalledGames.Steam],
            [
                Match("413150", "Stardew Valley", ByName(Stardew)),
                Match("377160", "Fallout 4", ByName(Fallout)),
                Match("1", "Not On A Site", ByName()),
            ]);

        Assert.Equal(
            [
                ("On this PC (Steam)", "Fallout 4", "Not in your library"),
                (null, "Stardew Valley", "21 mods in your library"),
                ("Your games", "Skyrim Special Edition", "14 mods in your library"),
            ],
            _picker.Entries.Select(entry => (entry.Heading, entry.Name, entry.Meta)));
    }

    [Fact]
    public void WithoutALibraryTheInstalledGamesSayNothingMore()
    {
        _picker.ShowInstalled([ScriptedInstalledGames.Steam], [Match("413150", "Stardew Valley", ByName(Stardew))]);

        Assert.Equal([("On this PC (Steam)", "Stardew Valley", string.Empty)], _picker.Entries.Select(entry => (entry.Heading, entry.Name, entry.Meta)));
    }

    [Fact]
    public void TypingFindsAGameByItsNameOrTheInstalledGamesAndListsItOnce()
    {
        _picker.ShowLibrary(Library((Skyrim, 3)));
        _picker.ShowInstalled([ScriptedInstalledGames.Steam], [Match("489830", "The Elder Scrolls V: Skyrim Special Edition", Picked(Skyrim))]);

        _picker.Query = "elder";
        string[] byInstalledName = [.. _picker.Entries.Select(entry => entry.Name)];
        _picker.Query = "sky";

        Assert.Equal(["Skyrim Special Edition"], byInstalledName);
        Assert.Equal([("On this PC (Steam)", "Skyrim Special Edition")], _picker.Entries.Select(entry => (entry.Heading, entry.Name)));
    }

    [Fact]
    public void AGameTwoInstalledGamesMatchIsListedOnce()
    {
        _picker.ShowInstalled(
            [ScriptedInstalledGames.Steam],
            [
                Match("489830", "The Elder Scrolls V: Skyrim Special Edition", Picked(Skyrim)),
                Match("611670", "The Elder Scrolls V: Skyrim VR", Picked(Skyrim)),
            ]);

        Assert.Equal(["Skyrim Special Edition"], _picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void ChoosingOneOfTheGamesANameMatchesPicksItForTheInstalledGame()
    {
        GameInfo expanded = new(Nexus("stardewvalleyexpanded"), "Stardew Valley");
        InstalledGameMatch stardew = Match("413150", "Stardew Valley", ByName(Stardew, expanded));
        _picker.ShowInstalled([ScriptedInstalledGames.Steam], [stardew, Match("377160", "Fallout 4", ByName(Fallout))]);
        (string Name, string Meta)[] shown = [.. _picker.Entries.Select(entry => (entry.Name, entry.Meta))];

        _picker.Entries.Single(entry => entry.Game == expanded.Game).PickCommand.Execute(null);
        _picker.Entries.Single(entry => entry.Game == Fallout.Game).PickCommand.Execute(null);

        Assert.Equal(
            [
                ("Fallout 4", string.Empty),
                ("Stardew Valley", "stardewvalley, one of the games of this name"),
                ("Stardew Valley", "stardewvalleyexpanded, one of the games of this name"),
            ],
            shown);
        Assert.Equal([(stardew.Game, expanded.Game)], _picked);
        Assert.Equal([expanded.Game, Fallout.Game], _chosen);
    }

    [Fact]
    public void AGamePickedOnASiteThatFindsItsGamesByNameIsKnownAndChosen()
    {
        _picker.ShowInstalled([ScriptedInstalledGames.Steam], [Match("1687950", "Persona 5 Royal", Picked(Royal), ByName())]);

        _picker.Entries.Single().PickCommand.Execute(null);

        Assert.Same(Royal, _picker.Known(Royal.Game));
        Assert.Equal([Royal.Game], _chosen);
        Assert.Empty(_picked);
        Assert.True(_picker.Entries.Single().ShowSite);
    }

    [Fact]
    public void ASiteChosenNarrowsTheInstalledGamesToItsGames()
    {
        _picker.ShowInstalled(
            [ScriptedInstalledGames.Steam],
            [Match("1687950", "Persona 5 Royal", Picked(Royal)), Match("413150", "Stardew Valley", ByName(Stardew))]);

        _picker.ShowSite(Banana);
        string[] banana = [.. _picker.Entries.Select(entry => entry.Name)];
        _picker.ShowSite(TestData.Nexus);
        string[] nexus = [.. _picker.Entries.Select(entry => entry.Name)];
        _picker.ShowSite(null);

        Assert.Equal(["Persona 5 Royal (PC)"], banana);
        Assert.Equal(["Stardew Valley"], nexus);
        Assert.Equal(["Persona 5 Royal (PC)", "Stardew Valley"], _picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void OpensWithInstalledGamesAlone()
    {
        GamePickerViewModel empty = new(_pictures.Pictures, new FakeSourceRegistry(new FakeSource(TestData.Nexus, "Nexus Mods")), _chosen.Add);
        empty.OpenCommand.Execute(null);
        bool openWithNothing = empty.IsOpen;

        empty.ShowInstalled([ScriptedInstalledGames.Steam], [Match("413150", "Stardew Valley", Picked(Stardew))]);
        empty.OpenCommand.Execute(null);

        Assert.False(openWithNothing);
        Assert.True(empty.IsOpen);
    }

    private static GameRef Nexus(string key) => new(TestData.Nexus, key);

    private static Dictionary<GameRef, int> Library(params (GameInfo Game, int Mods)[] games)
        => games.ToDictionary(game => game.Game.Game, game => game.Mods);

    private static InstalledGameMatch Match(string id, string name, params SiteMatch[] sites)
        => new(new InstalledGame("steam", id, name, Path.Combine(ScriptedInstalledGames.Steam.Folder, name)), sites);

    private static SiteMatch ByName(params GameInfo[] games) => new(games.FirstOrDefault()?.Game.Source ?? TestData.Nexus, games, Picked: false);

    private static SiteMatch Picked(GameInfo game) => new(game.Game.Source, [game], Picked: true);
}
