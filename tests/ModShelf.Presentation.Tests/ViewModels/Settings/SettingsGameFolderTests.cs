using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// A game's Folder row: where its files are, the library's folder or its own, how many and how much room they take;
/// Choose… moving them to a folder the user picks, once they confirm, and Move back to the library; a game without
/// files taking a folder at once.
/// </summary>
public sealed class SettingsGameFolderTests
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Ussep = TestData.Mod("266");
    private static readonly GameRef Skyrim = SkyUi.Game;
    private static readonly GameRef Stardew = TestData.Mod("2400", "stardewvalley").Game;
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Skyrim mods");

    private readonly SettingsHarness _harness = new();

    public SettingsGameFolderTests()
    {
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Library.Add(TestData.LibraryMod(SkyUi, "SkyUI", files: TestData.LibraryFile(SkyUi, "1", "SkyUI.7z", size: 3L << 20)));
        _harness.Library.Add(TestData.LibraryMod(
            Ussep,
            "USSEP",
            files: [TestData.LibraryFile(Ussep, "2", "USSEP.7z", size: 40L << 20), TestData.LibraryFile(Ussep, "3", "USSEP patch.7z", size: 1L << 20)]));

        // Watched only: no files to count.
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("3863"), "SkyUI Away"));
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    [Fact]
    public async Task SaysItsFilesAreInTheLibrarysFolderAndHowMany()
    {
        await StartOnAsync(Skyrim);

        Assert.Equal(
            (false, TestData.LibraryRoot, "Its 2 mods' files, 44.0 MB, are in the library's folder.", (string?)null),
            (Game.HasOwnRoot, Game.FolderPath, Game.FolderText, Game.FolderNote));
        Assert.False(Game.MoveBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaysWhenTheyreInAFolderOfTheirOwn()
    {
        Own(Skyrim, Folder);

        await StartOnAsync(Skyrim);

        Assert.Equal(
            (true, Folder, "Its 2 mods' files, 44.0 MB, are in a folder of their own. The folder pattern still says where in it."),
            (Game.HasOwnRoot, Game.FolderPath, Game.FolderText));
        Assert.True(Game.MoveBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task OneModAndNoneAreSaidSo()
    {
        _harness.Library.Change(Ussep, null);
        Own(Stardew, Folder);

        await StartOnAsync(Skyrim);
        string one = Game.FolderText;
        Screen.SelectedGame = Screen.Games.Single(row => row.Game == Stardew);

        Assert.Equal("Its mod's files, 3.00 MB, are in the library's folder.", one);
        Assert.Equal("Its mods' files go in a folder of their own. The folder pattern still says where in it.", Game.FolderText);
    }

    [Fact]
    public async Task AGameWithoutFilesTakesTheFolderAtOnce()
    {
        Own(Stardew, null);
        await StartOnAsync(Stardew);
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Game.ChooseFolderCommand.ExecuteAsync(null);
        (bool, string, string) chosen = (Game.HasOwnRoot, Game.FolderPath, Game.FolderText);
        await Game.MoveBackCommand.ExecuteAsync(null);

        // Its name isn't read, as the library has none of its mods.
        Assert.Equal(["Choose a folder for stardewvalley's files"], _harness.Dialogs.FolderPickers);
        Assert.Equal((true, Folder, "Its mods' files go in a folder of their own. The folder pattern still says where in it."), chosen);
        Assert.Equal((false, TestData.LibraryRoot, "Its mods' files go in the library's folder."), (Game.HasOwnRoot, Game.FolderPath, Game.FolderText));
        Assert.Equal([$"game {Stardew}", $"game {Stardew}"], _harness.Settings.Changes);
        Assert.Empty(_harness.Dialogs.MovingGames);
        Assert.Empty(_harness.Mover.Moves);
    }

    [Fact]
    public async Task ChoosingAFolderMovesItsFilesOnceTheUserConfirms()
    {
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Game.ChooseFolderCommand.ExecuteAsync(null);

        GameMoveViewModel moving = Assert.Single(_harness.Dialogs.MovingGames);
        Assert.Equal((Skyrim, "Skyrim Special Edition", GameMoveStage.Done), (moving.Game, moving.Name, moving.Stage));
        Assert.Equal([$"{Skyrim} {Folder}"], _harness.Mover.Moves);
        Assert.Equal(Folder, _harness.Settings.Stored.GameFor(Skyrim).Root);
        Assert.Equal((true, Folder, "Moved 3 files."), (Game.HasOwnRoot, Game.FolderPath, Game.FolderNote));
        Assert.Equal("1 setting overridden", Screen.Games.Single(row => row.Game == Skyrim).Note);
    }

    [Fact]
    public async Task MovingBackTakesTheFilesToTheLibrarysFolder()
    {
        Own(Skyrim, Folder);
        await StartOnAsync(Skyrim);

        await Game.MoveBackCommand.ExecuteAsync(null);

        Assert.Equal([$"{Skyrim} back"], _harness.Mover.Moves);
        Assert.Equal((false, TestData.LibraryRoot, "Moved 3 files."), (Game.HasOwnRoot, Game.FolderPath, Game.FolderNote));
        Assert.Empty(_harness.Dialogs.FolderPickers);
    }

    [Fact]
    public async Task ChoosingTheLibrarysFolderMovesThemBack()
    {
        Own(Skyrim, Folder);
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(TestData.LibraryRoot);

        await Game.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal([$"{Skyrim} back"], _harness.Mover.Moves);
        Assert.Null(_harness.Settings.Stored.GameFor(Skyrim).Root);
    }

    [Fact]
    public async Task TheFolderTheyreInAlreadyOrNoneChosenDoesNothing()
    {
        Own(Skyrim, Folder);
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Game.ChooseFolderCommand.ExecuteAsync(null);
        await Game.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(2, _harness.Dialogs.FolderPickers.Count);
        Assert.Empty(_harness.Dialogs.MovingGames);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task ClosingTheDialogMovesNothing()
    {
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(Folder);
        _harness.Dialogs.InMoveGame = moving => moving.Planning;

        await Game.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.MovingGames);
        Assert.Empty(_harness.Mover.Moves);
        Assert.Equal((false, TestData.LibraryRoot, (string?)null), (Game.HasOwnRoot, Game.FolderPath, Game.FolderNote));
    }

    [Theory]
    [InlineData(1, "Moved 3 files. One of its files wasn't on disk; it stays in the library, to download again.")]
    [InlineData(2, "Moved 3 files. 2 of its files weren't on disk; they stay in the library, to download again.")]
    public async Task FilesThatWerentOnDiskAreSaid(int missing, string note)
    {
        _harness.Mover.Missing = missing;
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Game.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(note, Game.FolderNote);
    }

    [Fact]
    public async Task WhatAMoveDidGoesWithTheNextOne()
    {
        await StartOnAsync(Skyrim);
        _harness.Dialogs.Folders.Enqueue(Folder);
        await Game.ChooseFolderCommand.ExecuteAsync(null);
        string? moved = Game.FolderNote;
        _harness.Dialogs.InMoveGame = moving => moving.Planning;

        await Game.MoveBackCommand.ExecuteAsync(null);

        Assert.Equal(("Moved 3 files.", (string?)null), (moved, Game.FolderNote));
    }

    [Fact]
    public async Task OneFileMovedAndNoneNeededAreSaidSo()
    {
        await StartOnAsync(Skyrim);
        _harness.Mover.Plan = plan => plan with { Files = 1 };
        _harness.Dialogs.Folders.Enqueue(Folder);

        await Game.ChooseFolderCommand.ExecuteAsync(null);
        string? one = Game.FolderNote;
        _harness.Mover.Plan = plan => plan with { Files = 0, AlreadyThere = 3 };
        await Game.MoveBackCommand.ExecuteAsync(null);

        Assert.Equal(("Moved 1 file.", "No file needed moving."), (one, Game.FolderNote));
    }

    [Fact]
    public async Task TheFilesShownFollowTheLibrary()
    {
        await StartOnAsync(Skyrim);

        _harness.Library.Change(SkyUi, null);
        Screen.OnShown();
        await Screen.GamesWork;
        string one = Game.FolderText;
        _harness.Library.Change(Ussep, null);
        Screen.OnShown();
        await Screen.GamesWork;

        Assert.Equal("Its mod's files, 41.0 MB, are in the library's folder.", one);
        Assert.Equal("Its mods' files go in the library's folder.", Game.FolderText);
    }

    private void Own(GameRef game, string? root)
    {
        _harness.Settings.Stored = _harness.Settings.Stored with
        {
            Games = _harness.Settings.Stored.Games.ToImmutableDictionary(AppSettings.GameComparer)
                .SetItem(game, new GameSettings { Root = root, Notify = root is null ? false : null }),
        };
    }

    private async Task StartOnAsync(GameRef game)
    {
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
        await Screen.ShownWork;
        Screen.SelectedGame = Screen.Games.Single(row => row.Game == game);
    }
}
