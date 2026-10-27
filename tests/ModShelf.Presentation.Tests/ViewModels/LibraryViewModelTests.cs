using ModShelf.Domain.Library;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class LibraryViewModelTests : IDisposable
{
    private static readonly LibraryMod SkyUi = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI");
    private static readonly LibraryMod Address = TestData.LibraryMod(TestData.Mod("3863"), "Address Library");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");
    private static readonly LibraryMod Ussep = TestData.LibraryMod(TestData.Mod("266"), "unofficial patch");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheGamesAndTheirModsByName()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";

        await _harness.StartAsync(SkyUi, Smapi, Ussep, Address);

        Assert.Equal(
            ["# Skyrim Special Edition", "Address Library", "SkyUI", "unofficial patch", "# Stardew Valley", "SMAPI"],
            _harness.Lines());
        Assert.Equal(["3 mods", "1 mod"], Screen.Rows.OfType<LibraryGameViewModel>().Select(game => game.ModsText));
        Assert.False(Screen.IsEmpty);
        Assert.Equal(0, Screen.Badge);
    }

    [Fact]
    public async Task GamesAreOrderedByTheNamesTheirSitesGiveOnceTheyArrive()
    {
        _harness.Catalog.GameNames["zgame"] = "A Game";
        _harness.Catalog.HoldGames = new TaskCompletionSource();
        LibraryMod other = TestData.LibraryMod(TestData.Mod("1", "zgame"), "Other");
        await _harness.StartAsync(SkyUi, other);
        IReadOnlyList<string> before = _harness.Lines();
        LibraryGameViewModel skyrim = Screen.Rows.OfType<LibraryGameViewModel>().First();

        // The names arrive on threads of their own, so they're held for the UI thread to show one after the other.
        _harness.UiThread.Inline = false;
        _harness.Catalog.HoldGames.SetResult();
        Assert.True(SpinWait.SpinUntil(() => _harness.UiThread.Pending == 2, TimeSpan.FromSeconds(10)));
        _harness.UiThread.RunPending();

        Assert.Equal(["# skyrimspecialedition", "SkyUI", "# zgame", "Other"], before);
        Assert.Equal(["# A Game", "Other", "# Skyrim Special Edition", "SkyUI"], _harness.Lines());
        Assert.Same(skyrim, Screen.Rows[2]);
    }

    [Fact]
    public async Task AGameGoesByItsKeyWhenItsSiteCantNameIt()
    {
        await _harness.StartAsync(Smapi);

        Assert.Equal(["# stardewvalley", "SMAPI"], _harness.Lines());
        Assert.Equal(
            ["Information: Couldn't read the name of game stardewvalley from nexus, so it goes by its key: No such game. "],
            _harness.Logger.Entries);
    }

    [Fact]
    public async Task AnUnexpectedFailureToNameAGameIsLoggedWithItsDetails()
    {
        _harness.Catalog.Refusal = new InvalidOperationException("Something broke.");

        await _harness.StartAsync(SkyUi);

        Assert.Equal(["# skyrimspecialedition", "SkyUI"], _harness.Lines());
        Assert.StartsWith(
            "Warning: Reading the name of game skyrimspecialedition from nexus failed, so it goes by its key System.InvalidOperationException: Something broke.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachGamesSiteIsAskedForItsNameOnce()
    {
        await _harness.StartAsync(SkyUi, Address);

        _harness.Library.Change(SkyUi.Mod, null);
        _harness.Library.Change(Address.Mod, null);
        _harness.Library.Change(SkyUi.Mod, SkyUi);

        Assert.Equal(["GetGameAsync skyrimspecialedition"], _harness.Catalog.Asked);
        Assert.Equal(["# Skyrim Special Edition", "SkyUI"], _harness.Lines());
    }

    [Fact]
    public async Task AnEmptyLibrarySaysSo()
    {
        await _harness.StartAsync();

        Assert.True(Screen.IsEmpty);
        Assert.False(Screen.NothingMatches);
        Assert.Empty(Screen.Rows);
    }

    [Fact]
    public async Task TheFilterShowsTheModsWhoseNamesHaveItAndTheirGames()
    {
        await _harness.StartAsync(SkyUi, Smapi, Ussep, Address);

        Screen.Filter = " SKY ";

        Assert.Equal(["# Skyrim Special Edition", "SkyUI"], _harness.Lines());
        Assert.Equal("1 mod", Assert.Single(Screen.Rows.OfType<LibraryGameViewModel>()).ModsText);
        Assert.False(Screen.NothingMatches);
    }

    [Fact]
    public async Task AFilterNothingMatchesSaysSo()
    {
        await _harness.StartAsync(SkyUi, Smapi);

        Screen.Filter = "zzz";
        (bool, string) nothing = (Screen.NothingMatches, Screen.NothingMatchesText);
        Screen.Filter = string.Empty;

        Assert.Equal((true, "No mod's name has “zzz”."), nothing);
        Assert.False(Screen.NothingMatches);
        Assert.Equal(4, Screen.Rows.Count);
    }

    [Fact]
    public async Task FollowsTheLibrarysChanges()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        await _harness.StartAsync(SkyUi, Address);
        LibraryModViewModel row = _harness.Row(SkyUi);
        row.IsExpanded = true;

        _harness.Library.Change(Smapi.Mod, Smapi);
        _harness.Library.Change(Address.Mod, null);
        _harness.Library.Change(SkyUi.Mod, TestData.LibraryMod(SkyUi.Mod, "SkyUI renamed", watched: false, TestData.LibraryFile(SkyUi.Mod, "1", "a.7z")));

        Assert.Equal(["# Skyrim Special Edition", "SkyUI renamed", "# Stardew Valley", "SMAPI"], _harness.Lines());
        Assert.Same(row, _harness.Row(SkyUi));
        Assert.True(row.IsExpanded);
        Assert.False(row.IsWatched);
    }

    [Fact]
    public async Task AGameWhoseLastModLeftGoes()
    {
        await _harness.StartAsync(SkyUi, Smapi);

        _harness.Library.Change(Smapi.Mod, null);

        Assert.Equal(["# Skyrim Special Edition", "SkyUI"], _harness.Lines());
    }

    [Fact]
    public async Task TheLastModLeavingEmptiesTheLibrary()
    {
        await _harness.StartAsync(SkyUi);

        _harness.Library.Change(SkyUi.Mod, null);

        Assert.True(Screen.IsEmpty);
        Assert.Empty(Screen.Rows);
    }

    [Fact]
    public async Task ChangesShowOnlyOnTheUiThread()
    {
        _harness.UiThread.Inline = false;
        await _harness.StartAsync(SkyUi);
        bool listedBeforeTheUiThreadRan = Screen.Rows.Count > 0;
        _harness.UiThread.RunPending();

        _harness.Library.Change(Address.Mod, Address);
        int beforeTheUiThreadRan = Screen.Rows.Count;
        _harness.UiThread.RunPending();

        Assert.False(listedBeforeTheUiThreadRan);
        Assert.Equal(2, beforeTheUiThreadRan);
        Assert.Equal(["# Skyrim Special Edition", "Address Library", "SkyUI"], _harness.Lines());
    }

    [Fact]
    public async Task NamesAreOrderedAsTheUsersCultureOrdersThem()
    {
        using CultureScope culture = new("sv-SE");
        LibraryMod zebra = TestData.LibraryMod(TestData.Mod("1"), "Zebra");
        LibraryMod oland = TestData.LibraryMod(TestData.Mod("2"), "Öland");

        await _harness.StartAsync(oland, zebra);

        Assert.Equal(["# Skyrim Special Edition", "Zebra", "Öland"], _harness.Lines());
    }

    [Fact]
    public async Task TheWorkEndsWhenTheAppCloses()
    {
        await _harness.StartAsync(SkyUi);

        _harness.Dispose();

        await _harness.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
