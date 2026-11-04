using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The same game on two sites in the Games category: each named with its site, its page saying where else it is and
/// offering that game's settings, which it takes only when the user says so. Adding a game by its key alone asks for its
/// site when more than one site has games.
/// </summary>
public sealed class SettingsSameNamedGamesTests
{
    private static readonly GameRef NexusStardew = TestData.Mod("2400", "stardewvalley").Game;
    private static readonly GameRef ExampleStardew = ExampleSite.StardewValley.Game;
    private static readonly GameRef Skyrim = TestData.Mod("12604").Game;

    private readonly SettingsHarness _harness = new(example: new ExampleSite());

    public SettingsSameNamedGamesTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    [Fact]
    public async Task EachGameOfTheSameNameIsNamedWithItsSite()
    {
        await StartWithLibraryAsync();

        Assert.Equal(
            [
                (Skyrim, "Skyrim Special Edition"),
                (ExampleStardew, "Stardew Valley (Example Mods)"),
                (NexusStardew, "Stardew Valley (Nexus Mods)"),
            ],
            Screen.Games.Select(row => (row.Game, row.Name)));
    }

    [Fact]
    public async Task AGamesPageSaysWhereElseItIsAndOffersThatGamesSettings()
    {
        await StartWithLibraryAsync();

        Choose(NexusStardew);

        Assert.Equal(("Stardew Valley (Nexus Mods)", "Also on Example Mods", true), (Game.Name, Game.AlsoOnText, Game.HasAlsoOn));
        GameCopyViewModel copy = Assert.Single(Game.CopyChoices);
        Assert.Equal((ExampleStardew, "Copy settings from Stardew Valley (Example Mods)"), (copy.Game, copy.Text));

        Choose(ExampleStardew);

        Assert.Equal("Also on Nexus Mods", Game.AlsoOnText);
        Assert.Equal(NexusStardew, Assert.Single(Game.CopyChoices).Game);
    }

    [Fact]
    public async Task AGameWithoutAnotherOfItsNameOffersNothing()
    {
        await StartWithLibraryAsync();

        Choose(Skyrim);

        Assert.Equal((string.Empty, false), (Game.AlsoOnText, Game.HasAlsoOn));
        Assert.Empty(Game.CopyChoices);
    }

    [Fact]
    public async Task CopyingAsksSayingWhatChangesThenTakesEveryOneOfTheOthersSettings()
    {
        GameSettings examples = new() { Folders = FolderPattern.Parse("{mod}/{file}"), Notify = false, Hidden = true };
        Store((ExampleStardew, examples), (NexusStardew, new GameSettings { OldVersions = OldVersionRule.KeepAll }));
        await StartWithLibraryAsync();
        Choose(NexusStardew);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Assert.Single(Game.CopyChoices).CopyCommand.ExecuteAsync(null);

        Assert.Equal(
            (
                "Copy the settings?",
                "Stardew Valley (Nexus Mods) will take the settings of Stardew Valley (Example Mods) in place of its own: folders, notices and hidden from the lists.",
                "Copy settings"),
            Assert.Single(_harness.Dialogs.Confirmations));
        Assert.Equal(examples, _harness.Settings.Stored.GameFor(NexusStardew));
        Assert.Equal(examples, _harness.Settings.Stored.GameFor(ExampleStardew));
        Assert.Equal(["copy example/stardew-valley to nexus/stardewvalley"], _harness.Settings.Changes);
        Assert.True(Game.IsHidden);
    }

    [Fact]
    public async Task CopyingFromAGameWithoutSettingsSaysTheGameWillUseEveryGames()
    {
        Store((NexusStardew, new GameSettings { Notify = false }));
        await StartWithLibraryAsync();
        Choose(NexusStardew);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Assert.Single(Game.CopyChoices).CopyCommand.ExecuteAsync(null);

        Assert.Equal(
            "Stardew Valley (Example Mods) has no settings of its own, so Stardew Valley (Nexus Mods) will use every game's in place of its own.",
            Assert.Single(_harness.Dialogs.Confirmations).Message);
        Assert.Empty(_harness.Settings.Stored.Games);
    }

    [Fact]
    public async Task CopyingSaysOnlyWhatTheOtherGameSetsAndNotHiddenWhenItsShown()
    {
        // The app forgets a shown game's Hidden, but settings.json may say "hidden": false.
        Store((ExampleStardew, new GameSettings { Notify = false, Hidden = false }));
        await StartWithLibraryAsync();
        Choose(NexusStardew);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Assert.Single(Game.CopyChoices).CopyCommand.ExecuteAsync(null);

        Assert.Equal(
            "Stardew Valley (Nexus Mods) will take the settings of Stardew Valley (Example Mods) in place of its own: notices.",
            Assert.Single(_harness.Dialogs.Confirmations).Message);
        Assert.False(Game.IsHidden);
    }

    [Fact]
    public async Task CopyingTakesTheOtherGamesPreviewsWhichItsPagesShowAtOnce()
    {
        Store((ExampleStardew, new GameSettings { Previews = PreviewLevel.Cards }));
        await StartWithLibraryAsync();
        Choose(NexusStardew);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Assert.Single(Game.CopyChoices).CopyCommand.ExecuteAsync(null);

        Assert.Equal(
            "Stardew Valley (Nexus Mods) will take the settings of Stardew Valley (Example Mods) in place of its own: previews.",
            Assert.Single(_harness.Dialogs.Confirmations).Message);
        Assert.Equal((true, PreviewLevel.Cards), (Game.HasOwnPreviews, Game.Previews));
        Assert.Equal(PreviewLevel.Cards, _harness.Pictures.Pictures.LevelOf(PreviewScreen.Library, NexusStardew));
    }

    [Fact]
    public async Task NotCopyingChangesNothing()
    {
        Store((ExampleStardew, new GameSettings { Hidden = true }));
        await StartWithLibraryAsync();
        Choose(NexusStardew);
        _harness.Dialogs.Confirm.Enqueue(false);

        await Assert.Single(Game.CopyChoices).CopyCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.Settings.Changes);
        Assert.Same(GameSettings.None, _harness.Settings.Stored.GameFor(NexusStardew));
    }

    [Fact]
    public async Task AGameAddedByItsKeyAloneAsksForItsSiteWhenMoreThanOneHasGames()
    {
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue("stardewvalley");
        _harness.Dialogs.Choices.Enqueue(1);

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Equal(("Which site?", "Which site's game is stardewvalley?"), (_harness.Dialogs.ChoiceQuestions[0].Title, _harness.Dialogs.ChoiceQuestions[0].Message));
        Assert.Equal(["Nexus Mods", "Example Mods"], Assert.Single(_harness.Dialogs.ChoiceQuestions).Choices);
        Assert.Equal(new GameRef(ExampleSite.SourceId, "stardewvalley"), Game.Game);
    }

    [Fact]
    public async Task AGameAddedByItsKeyAloneIsNotAddedWhenNoSiteIsChosen()
    {
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue("stardewvalley");
        _harness.Dialogs.Choices.Enqueue(null);

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Empty(Screen.Games);
        Assert.Null(Screen.Game);
    }

    [Fact]
    public async Task AGameAddedWithItsSiteIsAddedWithoutAQuestion()
    {
        await StartAsync();
        _harness.Dialogs.Answers.Enqueue(" example/pixel-farm ");

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.ChoiceQuestions);
        Assert.Equal(ExampleSite.PixelFarm.Game, Game.Game);
    }

    [Fact]
    public async Task AGameAddedByAKeyOnlyOneSitesListedGameHasIsThatGame()
    {
        await StartWithLibraryAsync();
        _harness.Dialogs.Answers.Enqueue("Stardew-Valley");

        await Screen.AddGameCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.ChoiceQuestions);
        Assert.Equal(ExampleStardew, Game.Game);
        Assert.Equal(3, Screen.Games.Count);
    }

    [Fact]
    public async Task AnotherSitesGamesFolderPatternIsShownWithItsSite()
    {
        Store((ExampleStardew, new GameSettings { Folders = FolderPattern.Parse("{source}/{game}/{file}") }));
        await StartWithLibraryAsync();

        Choose(ExampleStardew);

        Assert.Equal(
            "For example: " + Path.Combine(_harness.Settings.DefaultLibraryRoot, "example", "stardew-valley", "SkyUI_5_2_SE.7z"),
            Game.Folders.Preview);
    }

    private void Store(params (GameRef Game, GameSettings Own)[] games)
        => _harness.Settings.Stored = new AppSettings { Games = games.ToImmutableDictionary(game => game.Game, game => game.Own, AppSettings.GameComparer) };

    private void Choose(GameRef game) => Screen.SelectedGame = Screen.Games.Single(row => row.Game == game);

    private async Task StartAsync()
    {
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
    }

    /// <summary>
    /// Starts the screen with Skyrim Special Edition and Stardew Valley from Nexus Mods, and Stardew Valley from the example
    /// site, in the library.
    /// </summary>
    private async Task StartWithLibraryAsync()
    {
        _harness.Library.Add(
            TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"),
            TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"),
            TestData.LibraryMod(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), "Better Crops"));
        await StartAsync();
    }
}
