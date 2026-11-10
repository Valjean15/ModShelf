using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse with a site that finds its games by name, as GameBanana does, beside Nexus Mods: each site's games shown as it
/// answers, the library's of the site that finds them by name offered by the names it gives them, and others found as
/// they're typed.
/// </summary>
public sealed class BrowseGamesByNameTests : IDisposable
{
    private static readonly ModRef Protagonist = new(BrowseHarness.Royal.Game, "440088");
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new(banana: true);

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TheLibrarysGamesOfASiteThatFindsThemByNameAreOfferedByTheirNames()
    {
        await _harness.StartAsync(TestData.LibraryMod(Protagonist, "P5R Female Protagonist"), TestData.LibraryMod(SkyUi, "SkyUI"));

        await _harness.ShowAsync();

        Assert.Equal(
            [("Persona 5 Royal (PC)", "1 mod in your library"), ("Skyrim Special Edition", "1 mod in your library")],
            Screen.Picker.Entries.Select(entry => (entry.Name, entry.Meta)));
        Assert.Contains(Screen.Games, choice => choice == new GameChoice(BrowseHarness.Royal.Game, "Persona 5 Royal (PC) (GameBanana)"));
        Assert.Equal("Type to find any of the 3 games on Nexus Mods, or a game on GameBanana by its name.", Screen.Picker.Hint);
        Assert.DoesNotContain($"GetGamesAsync {BrowseHarness.Banana}", _harness.Lookup.Asked);
    }

    [Fact]
    public async Task ItStartsOnTheGameTheLibraryHasTheMostModsOfWhateverItsSite()
    {
        await _harness.StartAsync(
            TestData.LibraryMod(Protagonist, "P5R Female Protagonist"),
            TestData.LibraryMod(new ModRef(BrowseHarness.Royal.Game, "414644"), "Restored Outfits Dialogue"),
            TestData.LibraryMod(SkyUi, "SkyUI"));

        await _harness.ShowAsync();

        Assert.Equal(BrowseHarness.Royal.Game, Screen.Game?.Game);
    }

    [Fact]
    public async Task ASlowSiteKeepsNoneOfTheOthersGamesWaiting()
    {
        TaskCompletionSource nexus = new();
        _harness.Lookup.HeldGames[TestData.Nexus] = nexus;
        await _harness.StartAsync(TestData.LibraryMod(Protagonist, "P5R Female Protagonist"));

        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Browse);
        await WaitForAsync(() => Screen.Games.Any(choice => choice.Game == BrowseHarness.Royal.Game));
        GameRef[] whileNexusReads = [.. Screen.Games.Select(choice => choice.Game)];
        bool reading = Screen.IsReadingGames;
        nexus.SetResult();
        await Screen.GamesWork;

        Assert.Equal([BrowseHarness.Royal.Game], whileNexusReads);
        Assert.True(reading);
        Assert.Equal(4, Screen.Games.Count);
        Assert.False(Screen.IsReadingGames);
    }

    [Fact]
    public async Task AGameFoundByNameCanBeChosenAndIsThenOffered()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        Screen.Picker.Query = "zelda";
        await Screen.Picker.Finding;
        Screen.Picker.ChooseCommand.Execute(null);

        Assert.Equal(new GameChoice(BrowseHarness.Zelda.Game, "The Legend of Zelda (GameBanana)"), Screen.Game);
        Assert.Contains(Screen.Game, Screen.Games);
        Assert.Equal("The Legend of Zelda", Screen.Picker.Picked?.Name);
    }

    [Fact]
    public async Task QuickSearchOpensAGameFoundByNameThatIsntOfferedYet()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        _harness.Screens.OpenInBrowse(BrowseHarness.Persona.Game);
        await Screen.GamesWork;

        Assert.Equal(new GameChoice(BrowseHarness.Persona.Game, "Persona 5 (GameBanana)"), Screen.Game);
    }

    [Fact]
    public async Task AGameOfTheSiteNewToTheLibraryIsOffered()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        await _harness.ShowAsync();

        _harness.Library.Change(Protagonist, TestData.LibraryMod(Protagonist, "P5R Female Protagonist"));
        await Screen.GamesWork;

        Assert.Equal(["Persona 5 Royal (PC)", "Skyrim Special Edition"], Screen.Picker.Entries.Select(entry => entry.Name));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
