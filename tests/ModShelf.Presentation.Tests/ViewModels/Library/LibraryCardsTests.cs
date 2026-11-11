using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library's games and a game's mods as cards, in rows the view builds only while they show.
/// </summary>
public sealed class LibraryCardsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef First = TestData.Mod("1", "zgame");
    private static readonly ModRef Second = TestData.Mod("2", "zgame");
    private static readonly ModRef Third = TestData.Mod("3", "zgame");

    private readonly LibraryHarness _harness = new();

    public LibraryCardsTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheGamesAsCardsInRows()
    {
        await StartAsync();

        Screen.GameCards.Columns = 2;

        Assert.Equal(["zgame | Skyrim Special Edition", "Stardew Valley"], Lines(Screen.GameCards));
        Assert.Equal<object>(Screen.Games, Screen.GameCards.Rows.SelectMany(row => row.Cards));
    }

    [Fact]
    public async Task ShowsAGamesModsAsCardsInRowsUntilItsClosed()
    {
        await StartAsync();
        Screen.ModCards.Columns = 2;

        _harness.Game(First.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        string[] open = Lines(Screen.ModCards);
        Screen.ShowGamesCommand.Execute(null);

        Assert.Equal(["Z1 | Z2", "Z3"], open);
        Assert.Empty(Screen.ModCards.Rows);
    }

    [Fact]
    public async Task TheGamesSortedAgainMoveInTheirRowsOnceSorted()
    {
        await StartAsync();
        Screen.GameCards.Columns = 2;
        List<string[]> whileSorting = [];
        Screen.Games.CollectionChanged += (_, _) => whileSorting.Add(Lines(Screen.GameCards));

        Screen.SelectedGameOrder = Screen.GameOrders.Single(choice => choice.Value == GameOrder.Name);

        Assert.NotEmpty(whileSorting);
        Assert.All(whileSorting, rows => Assert.Equal(["zgame | Skyrim Special Edition", "Stardew Valley"], rows));
        Assert.Equal(["Skyrim Special Edition | Stardew Valley", "zgame"], Lines(Screen.GameCards));
    }

    [Fact]
    public async Task AGamesModsSortedAgainMoveInTheirRowsOnceSorted()
    {
        await StartAsync();
        Screen.ModCards.Columns = 2;
        _harness.Game(First.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        List<string[]> whileSorting = [];
        Screen.GameMods.CollectionChanged += (_, _) => whileSorting.Add(Lines(Screen.ModCards));

        Screen.SelectedModOrder = Screen.ModOrders.Single(choice => choice.Value == ModOrder.AddedToLibrary);

        Assert.NotEmpty(whileSorting);
        Assert.All(whileSorting, rows => Assert.Equal(["Z1 | Z2", "Z3"], rows));
        Assert.Equal(["Z3 | Z2", "Z1"], Lines(Screen.ModCards));
    }

    /// <summary>
    /// Three games: zgame with three mods added in turn, Skyrim Special Edition with two, Stardew Valley with one.
    /// </summary>
    private Task StartAsync() => _harness.StartAsync(
        TestData.LibraryMod(SkyUi, "SkyUI"),
        TestData.LibraryMod(Address, "Address Library"),
        TestData.LibraryMod(Smapi, "SMAPI"),
        Added(First, "Z1", daysAgo: 9),
        Added(Second, "Z2", daysAgo: 5),
        Added(Third, "Z3", daysAgo: 1));

    private static LibraryMod Added(ModRef mod, string name, int daysAgo)
        => LibraryMod.Restore(mod, name, null, null, isWatched: true, TestData.Now.AddDays(-daysAgo), null, []);

    private static string[] Lines(CardRows rows)
        => [.. rows.Rows.Select(row => string.Join(" | ", row.Cards.Select(Name)))];

    private static string Name(object card) => card switch
    {
        LibraryGameViewModel game => game.Name,
        LibraryModViewModel mod => mod.Name,
        _ => throw new InvalidOperationException("Not a card of the Library."),
    };
}
