using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Games;

/// <summary>
/// The game picker: the library's games, the one with the most mods first; the sites' others whose names have what's
/// typed; Browse's recent searches while nothing is; and the arrow keys, Enter and Escape.
/// </summary>
public sealed class GamePickerTests
{
    private static readonly Uri StardewCover = new("https://images.example.com/stardew.webp");
    private static readonly Uri StarfieldCover = new("https://images.example.com/starfield.webp");
    private static readonly GameInfo Stardew = new(Nexus("stardewvalley"), "Stardew Valley", coverUrl: StardewCover);
    private static readonly GameInfo Skyrim = new(Nexus("skyrimspecialedition"), "Skyrim Special Edition");
    private static readonly GameInfo Fallout = new(Nexus("fallout4"), "Fallout 4");
    private static readonly GameInfo Starfield = new(Nexus("starfield"), "Starfield", coverUrl: StarfieldCover);
    private static readonly GameInfo ExampleStardew = ExampleSite.StardewValley;

    private readonly TestPictures _pictures = new();
    private readonly List<GameRef> _chosen = [];
    private readonly List<string> _searched = [];
    private readonly List<string> _typed = [];
    private readonly GamePickerViewModel _picker;

    public GamePickerTests()
    {
        FakeSourceRegistry sources = new(new FakeSource(TestData.Nexus, "Nexus Mods"), new FakeSource(ExampleSite.SourceId, "Example Mods"));
        _picker = new GamePickerViewModel(_pictures.Pictures, sources, _chosen.Add, _searched.Add, _typed.Add);
    }

    [Fact]
    public void WhileNothingsTypedItListsYourGamesTheOneWithTheMostModsFirstAndSaysHowManyItFinds()
    {
        _picker.ShowGames([Fallout, Stardew, Skyrim, Starfield]);
        _picker.ShowLibrary(Library((Skyrim, 14), (Stardew, 21), (Fallout, 1)));

        Assert.Equal(
            [
                ("Your games", "Stardew Valley", "21 mods in your library"),
                (null, "Skyrim Special Edition", "14 mods in your library"),
                (null, "Fallout 4", "1 mod in your library"),
            ],
            _picker.Entries.Select(entry => (entry.Heading, entry.Name, entry.Meta)));
        Assert.Equal("Type to find any of the 4 games on Nexus Mods.", _picker.Hint);
        Assert.Null(_picker.NothingFound);
        Assert.False(_picker.Entries[0].ShowSite);
    }

    [Fact]
    public void YourGamesOfTheSameCountGoByName()
    {
        _picker.ShowGames([Fallout, Stardew, Skyrim]);

        _picker.ShowLibrary(Library((Stardew, 2), (Fallout, 2), (Skyrim, 3)));

        Assert.Equal(["Skyrim Special Edition", "Fallout 4", "Stardew Valley"], Names());
    }

    [Fact]
    public void TypingFindsYourGamesThenTheOthersThoseStartingWithItFirst()
    {
        GameInfo valleyLife = new(Nexus("valleylife"), "Valley Life");
        _picker.ShowGames([Stardew, Skyrim, Starfield, valleyLife, ExampleStardew]);
        _picker.ShowLibrary(Library((Stardew, 3)));

        _picker.Query = "valley";

        Assert.Equal(
            [
                ("Your games", "Stardew Valley", "Nexus Mods", "3 mods in your library"),
                ("Other games on Example Mods and Nexus Mods", "Valley Life", "Nexus Mods", "Not in your library"),
                (null, "Stardew Valley", "Example Mods", "Not in your library"),
            ],
            _picker.Entries.Select(entry => (entry.Heading, entry.Name, entry.SiteName, entry.Meta)));
        Assert.All(_picker.Entries, entry => Assert.True(entry.ShowSite));
        Assert.True(_picker.IsOpen);
        Assert.Null(_picker.Hint);
    }

    [Fact]
    public void SitesGoOnceTheGamesOfferedAreOfOneSite()
    {
        _picker.ShowGames([Stardew, ExampleStardew]);
        _picker.Query = "stardew";
        GamePickerEntryViewModel stardew = _picker.Entries.Single(entry => entry.Game == Stardew.Game);

        _picker.ShowGames([Stardew]);

        Assert.False(stardew.ShowSite);
        Assert.Same(stardew, Assert.Single(_picker.Entries));
    }

    [Fact]
    public void WithoutLibraryGamesTheOthersAreTheSitesGamesAndSayNothingMore()
    {
        _picker.ShowGames([Stardew, Starfield]);

        _picker.Query = "star";

        Assert.Equal([("Games on Nexus Mods", string.Empty), (null, string.Empty)], _picker.Entries.Select(entry => (entry.Heading, entry.Meta)));
    }

    [Fact]
    public void SaysWhenNoGamesNameHasWhatsTyped()
    {
        _picker.ShowGames([Stardew, Skyrim]);
        _picker.ShowLibrary(Library((Stardew, 1)));

        _picker.Query = "dew";

        Assert.Empty(_picker.Entries);
        Assert.Equal("No game's name has “dew”.", _picker.NothingFound);
        Assert.Null(_picker.Highlighted);
    }

    [Fact]
    public void WithNoGamesToFindItSaysNothingOfWhatsTyped()
    {
        _picker.Query = "notagame";

        Assert.Empty(_picker.Entries);
        Assert.Equal(((string?)null, (string?)null), (_picker.NothingFound, _picker.Hint));
    }

    [Fact]
    public void ListsFiftyOtherGamesAtMostSayingHowManyMore()
    {
        _picker.ShowGames(Enumerable.Range(1, 60).Select(number => new GameInfo(Nexus($"game{number}"), $"Game {number}")));

        _picker.Query = "game";

        Assert.Equal(GamePickerViewModel.MostOthers, _picker.Entries.Count);
        Assert.Equal("10 more: type more of the name.", _picker.MoreText);

        _picker.Query = "game 6";

        Assert.Null(_picker.MoreText);
    }

    [Fact]
    public void TheArrowKeysMoveFromGameToGameAndEnterChooses()
    {
        _picker.ShowGames([Stardew, Skyrim, Fallout]);
        _picker.ShowLibrary(Library((Stardew, 3), (Skyrim, 2), (Fallout, 1)));
        _picker.OpenCommand.Execute(null);

        Assert.Same(_picker.Entries[0], _picker.Highlighted);

        _picker.MoveDownCommand.Execute(null);
        _picker.MoveDownCommand.Execute(null);
        _picker.MoveDownCommand.Execute(null);

        Assert.Same(_picker.Entries[2], _picker.Highlighted);
        Assert.Equal([false, false, true], _picker.Entries.Select(entry => entry.IsHighlighted));

        _picker.MoveUpCommand.Execute(null);
        _picker.ChooseCommand.Execute(null);

        Assert.Equal([Skyrim.Game], _chosen);
        Assert.Equal((Skyrim.Game, false, string.Empty), (_picker.Picked?.Game, _picker.IsOpen, _picker.Query));
        Assert.True(_picker.HasPicked);
    }

    [Fact]
    public void FocusingOpensTheListOnceThereAreGamesOrRecentSearches()
    {
        _picker.OpenCommand.Execute(null);

        Assert.False(_picker.IsOpen);

        _picker.ShowRecent(["skyui"]);
        _picker.OpenCommand.Execute(null);

        Assert.True(_picker.IsOpen);

        _picker.IsOpen = false;
        _picker.ShowRecent([]);
        _picker.ShowGames([Stardew]);
        _picker.OpenCommand.Execute(null);

        Assert.True(_picker.IsOpen);
    }

    [Fact]
    public void TheArrowsOpenTheListFirst()
    {
        _picker.ShowGames([Stardew, Skyrim]);
        _picker.ShowLibrary(Library((Stardew, 2), (Skyrim, 1)));

        _picker.MoveDownCommand.Execute(null);

        Assert.True(_picker.IsOpen);
        Assert.Same(_picker.Entries[0], _picker.Highlighted);

        _picker.MoveUpCommand.Execute(null);

        Assert.Same(_picker.Entries[0], _picker.Highlighted);
    }

    [Fact]
    public void TheGameHighlightedStaysWhileItsListed()
    {
        _picker.ShowGames([Stardew, Starfield, Skyrim]);
        _picker.Query = "s";
        _picker.MoveDownCommand.Execute(null);
        _picker.MoveDownCommand.Execute(null);
        GamePickerEntryViewModel highlighted = Assert.IsType<GamePickerEntryViewModel>(_picker.Highlighted);

        _picker.Query = "st";

        Assert.Equal(["Stardew Valley", "Starfield"], Names());
        Assert.Same(highlighted, _picker.Highlighted);
        Assert.Equal(Starfield.Game, highlighted.Game);

        _picker.Query = "sky";

        Assert.Equal(Skyrim.Game, _picker.Highlighted?.Game);
        Assert.False(highlighted.IsHighlighted);
    }

    [Fact]
    public void ClickingAGameChoosesIt()
    {
        _picker.ShowGames([Stardew, Skyrim]);
        _picker.Query = "sky";

        _picker.Entries[0].PickCommand.Execute(null);

        Assert.Equal([Skyrim.Game], _chosen);
        Assert.False(_picker.IsOpen);
    }

    [Fact]
    public void EnterWithNoGameListedHandsOnWhatsTyped()
    {
        _picker.ShowGames([Stardew]);

        _picker.Query = " somegame ";
        _picker.ChooseCommand.Execute(null);

        Assert.Equal(["somegame"], _typed);
        Assert.Empty(_chosen);

        _picker.Query = "  ";
        _picker.ChooseCommand.Execute(null);

        Assert.Single(_typed);
    }

    [Fact]
    public void ClosingForgetsWhatsTypedAndKeepsTheGameChosen()
    {
        _picker.ShowGames([Stardew, Skyrim]);
        _picker.ShowPicked(Skyrim.Game);
        _picker.Query = "star";

        _picker.CloseCommand.Execute(null);

        Assert.Equal((false, string.Empty, Skyrim.Game), (_picker.IsOpen, _picker.Query, _picker.Picked?.Game));
        Assert.False(_picker.CloseCommand.CanExecute(null));
        Assert.Empty(_chosen);
    }

    [Fact]
    public void ItsCrossTakesTheChipAwayUntilAnotherIsChosenOrTheListCloses()
    {
        _picker.ShowGames([Stardew, Skyrim]);
        _picker.ShowPicked(Skyrim.Game);

        _picker.ClearCommand.Execute(null);

        Assert.Equal((false, true), (_picker.HasPicked, _picker.IsOpen));

        _picker.IsOpen = false;

        Assert.Equal(Skyrim.Game, _picker.Picked?.Game);

        _picker.ClearCommand.Execute(null);
        _picker.Query = "star";
        _picker.ChooseCommand.Execute(null);

        Assert.Equal(Stardew.Game, _picker.Picked?.Game);
    }

    [Fact]
    public void AGameTheSitesDontListHasNoChip()
    {
        _picker.ShowGames([Stardew]);

        _picker.ShowPicked(Skyrim.Game);

        Assert.False(_picker.HasPicked);

        _picker.ShowGames([Stardew, Skyrim]);

        Assert.Equal(Skyrim.Game, _picker.Picked?.Game);
    }

    [Fact]
    public async Task YourGamesAndTheChosenOneShowTheirCoversTheOthersTheirInitials()
    {
        _picker.ShowGames([Stardew, Starfield]);
        _picker.ShowLibrary(Library((Stardew, 1)));
        _picker.Query = "star";

        await Task.WhenAll(_picker.Entries.Select(entry => entry.Cover.Showing));

        Assert.Equal([StardewCover], _pictures.Cache.Asked);
        Assert.Equal(("S", "S"), (_picker.Entries[0].Initial, _picker.Entries[1].Initial));

        _picker.ShowPicked(Starfield.Game);
        await _picker.Picked!.Cover.Showing;

        Assert.Equal([StardewCover, StarfieldCover], _pictures.Cache.Asked);
    }

    [Fact]
    public void BrowsesRecentSearchesShowWhileNothingsTypedAndSearchAgain()
    {
        _picker.ShowGames([Stardew]);
        _picker.ShowRecent(["skyui", "armour"]);
        _picker.OpenCommand.Execute(null);

        Assert.Equal(["skyui", "armour"], _picker.Recent.Select(search => search.Text));
        Assert.True(_picker.HasRecent);

        _picker.Query = "s";

        Assert.False(_picker.HasRecent);

        _picker.Query = string.Empty;
        _picker.Recent[1].SearchCommand.Execute(null);

        Assert.Equal(["armour"], _searched);
        Assert.False(_picker.IsOpen);
    }

    [Fact]
    public void APickerThatDoesntSearchHasNoRecentSearches()
    {
        GamePickerViewModel picker = new(_pictures.Pictures, new FakeSourceRegistry(new FakeSource(TestData.Nexus, "Nexus Mods")), _chosen.Add);

        picker.ShowRecent(["skyui"]);

        Assert.Empty(picker.Recent);
        Assert.False(picker.HasRecent);
    }

    private static GameRef Nexus(string key) => new(TestData.Nexus, key);

    private static Dictionary<GameRef, int> Library(params (GameInfo Game, int Mods)[] games)
        => games.ToDictionary(game => game.Game.Game, game => game.Mods);

    private string[] Names() => [.. _picker.Entries.Select(entry => entry.Name)];
}
