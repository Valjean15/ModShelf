using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The dialog that moves a game's files: what would move, then the move with how far it is, and Stop; a drive without
/// room, or a folder the files can't go to, refused, with another folder to choose; a move that stopped said, to try
/// again.
/// </summary>
public sealed class GameMoveViewModelTests
{
    private static readonly GameRef Skyrim = TestData.Mod("12604").Game;
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Skyrim mods");
    private static readonly string Other = Path.Combine(Path.GetTempPath(), "Other mods");

    private readonly InMemorySettingsService _settings = new();
    private readonly ScriptedLibraryMover _mover;
    private readonly Queue<string?> _folders = new();
    private readonly List<string> _pickers = [];
    private readonly QueuedUiThread _uiThread = new();
    private readonly RecordingLogger<SettingsViewModel> _logger = new();
    private int _closes;

    public GameMoveViewModelTests()
    {
        _mover = new ScriptedLibraryMover(_settings);
    }

    [Fact]
    public async Task SaysWhatWouldMoveAndAsks()
    {
        _mover.Plan = plan => plan with { FreeSpace = 412L << 30, SameDrive = false, AlreadyThere = 3, CopiesKept = 1, Missing = 1 };

        GameMoveViewModel moving = await PlannedAsync(Folder);

        string room = GameMoveViewModel.DriveOf(Folder) is { } drive ? $"Room on {drive}" : "Room there";
        Assert.Equal(
            [
                ("From", TestData.LibraryRoot),
                ("To", Folder),
                ("Moving", "3 files, 2.93 KB"),
                (room, "412 GB free"),
                ("Already there", "3 files, kept as they are; 1 copy stays in the old folder too"),
                ("Missing", "1 file isn't on disk, so it can't move"),
            ],
            moving.Facts.Select(fact => (fact.Label, fact.Value)));
        Assert.Equal((GameMoveStage.Asking, "Move Skyrim Special Edition's files?"), (moving.Stage, moving.Title));
        Assert.Equal("Stopping puts back every file moved, and Skyrim Special Edition stays where it was.", moving.StopNote);
        Assert.Equal((true, true, "Cancel", (string?)null), (moving.ShowsStopNote, moving.MoveCommand.CanExecute(null), moving.CloseText, moving.Message));
        Assert.Empty(_mover.Moves);
    }

    [Fact]
    public async Task OneFileAndManyAreSaidSo()
    {
        _mover.Plan = plan => plan with { Files = 1, Bytes = 1000, AlreadyThere = 1, Missing = 2 };

        GameMoveViewModel moving = await PlannedAsync(Folder);

        Assert.Equal(
            [("From", TestData.LibraryRoot), ("To", Folder), ("Moving", "1 file, 1000 B"), ("Already there", "1 file, kept as it is"), ("Missing", "2 files aren't on disk, so they can't move")],
            moving.Facts.Select(fact => (fact.Label, fact.Value)));
    }

    [Fact]
    public async Task NoFilesMovingAndCopiesKeptAreSaidSo()
    {
        _mover.Plan = plan => plan with { Files = 0, Bytes = 0, AlreadyThere = 4, CopiesKept = 2 };

        GameMoveViewModel moving = await PlannedAsync(Folder);

        Assert.Equal("No files", moving.Facts.Single(fact => fact.Label == "Moving").Value);
        Assert.Equal("4 files, kept as they are; 2 copies stay in the old folder too", moving.Facts.Single(fact => fact.Label == "Already there").Value);
    }

    [Fact]
    public async Task TheRoomIsntSaidOnTheSameDriveOrWhenItsNotKnown()
    {
        _mover.Plan = plan => plan with { FreeSpace = 1L << 30 };
        GameMoveViewModel same = await PlannedAsync(Folder);
        _mover.Plan = plan => plan with { SameDrive = false };
        GameMoveViewModel unknown = await PlannedAsync(Folder);

        Assert.Equal(["From", "To", "Moving"], same.Facts.Select(fact => fact.Label));
        Assert.Equal(["From", "To", "Moving"], unknown.Facts.Select(fact => fact.Label));
    }

    [Fact]
    public async Task ADriveWithoutRoomIsRefusedAndAnotherFolderCanBeChosen()
    {
        _mover.Plan = plan => plan.To == Folder ? plan with { Bytes = 18L << 30, FreeSpace = 6L << 30, SameDrive = false } : plan;
        GameMoveViewModel moving = await PlannedAsync(Folder);

        (GameMoveStage, string, string?, bool) refused = (moving.Stage, moving.Title, moving.Message, moving.OffersAnotherFolder);
        (int, bool) asking = (moving.Facts.Count, moving.MoveCommand.CanExecute(null));
        _folders.Enqueue(Other);
        await moving.ChooseAnotherCommand.ExecuteAsync(null);

        string? drive = GameMoveViewModel.DriveOf(Folder);
        Assert.Equal(
            drive is null
                ? (GameMoveStage.NoRoom, "There isn't room there", "Skyrim Special Edition's files need 18.0 GB, and that folder's drive has 6.00 GB free. Choose another folder, or make room there first.", true)
                : (GameMoveStage.NoRoom, $"There isn't room on {drive}", $"Skyrim Special Edition's files need 18.0 GB, and {drive} has 6.00 GB free. Choose another folder, or make room on {drive} first.", true),
            refused);
        Assert.Equal((0, false), asking);
        Assert.Equal(["Choose a folder for Skyrim Special Edition's files"], _pickers);
        Assert.Equal((GameMoveStage.Asking, Other, (string?)null), (moving.Stage, moving.Facts[1].Value, moving.Message));

        await moving.MoveCommand.ExecuteAsync(null);

        Assert.Equal([$"{Skyrim} {Other}"], _mover.Moves);
    }

    [Fact]
    public async Task NoFolderChosenLeavesItRefused()
    {
        _mover.Plan = plan => plan with { FreeSpace = 0, SameDrive = false };
        GameMoveViewModel moving = await PlannedAsync(Folder);

        await moving.ChooseAnotherCommand.ExecuteAsync(null);

        Assert.Equal(GameMoveStage.NoRoom, moving.Stage);
    }

    [Fact]
    public async Task AFolderTheFilesCantGoToIsRefused()
    {
        _mover.Refusal = new InvalidOperationException("That folder is inside Fallout 4's folder.");

        GameMoveViewModel moving = await PlannedAsync(Folder);

        Assert.Equal(
            (GameMoveStage.Refused, "Skyrim Special Edition's files can't move", "That folder is inside Fallout 4's folder.", true),
            (moving.Stage, moving.Title, moving.Message, moving.ChooseAnotherCommand.CanExecute(null)));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLoggedAndSaidPlainly()
    {
        _mover.Refusal = new NotSupportedException("A bug.");

        GameMoveViewModel moving = await PlannedAsync(Folder);

        Assert.Equal((GameMoveStage.Refused, "Something went wrong."), (moving.Stage, moving.Message?[..21]));
        Assert.StartsWith($"Error: Moving the files of {Skyrim} failed System.NotSupportedException: A bug.", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovingSaysHowFarItIsThenCloses()
    {
        _mover.Hold = new TaskCompletionSource();
        GameMoveViewModel moving = await PlannedAsync(Folder);

        Task move = moving.MoveCommand.ExecuteAsync(null);

        Assert.Equal((GameMoveStage.Moving, "Moving Skyrim Special Edition's files"), (moving.Stage, moving.Title));
        Assert.Equal(("1 of 3 files · 1000 B of 2.93 KB", 33.3), (moving.ProgressText, Math.Round(moving.ProgressPercent, 1)));
        Assert.Equal((false, false, true, true), (moving.CanClose, moving.CloseCommand.CanExecute(null), moving.StopCommand.CanExecute(null), moving.ShowsStopNote));

        _mover.Hold.SetResult();
        await move;

        Assert.Equal((GameMoveStage.Done, 1), (moving.Stage, _closes));
        Assert.Equal(new LibraryMove(TestData.LibraryRoot, Folder, 3, 0, 0), moving.Result);
        Assert.Equal(("3 of 3 files · 2.93 KB of 2.93 KB", 100.0), (moving.ProgressText, moving.ProgressPercent));
    }

    [Fact]
    public async Task ProgressToldOffTheUiThreadShowsOnIt()
    {
        _uiThread.Inline = false;
        _mover.Hold = new TaskCompletionSource();
        GameMoveViewModel moving = await PlannedAsync(Folder);

        Task move = moving.MoveCommand.ExecuteAsync(null);
        string before = moving.ProgressText;
        _uiThread.RunPending();

        Assert.Equal(("0 of 3 files · 0 B of 2.93 KB", "1 of 3 files · 1000 B of 2.93 KB"), (before, moving.ProgressText));

        _mover.Hold.SetResult();
        await move;
    }

    [Fact]
    public async Task StoppingPutsTheFilesBackAndSaysSo()
    {
        _mover.Hold = new TaskCompletionSource();
        GameMoveViewModel moving = await PlannedAsync(Folder);
        Task move = moving.MoveCommand.ExecuteAsync(null);

        moving.StopCommand.Execute(null);
        await move;

        Assert.Equal(
            (GameMoveStage.Stopped, "The move stopped", $"The move was stopped. Every file is back where it was, and {Skyrim} stays in {TestData.LibraryRoot}."),
            (moving.Stage, moving.Title, moving.Message));
        Assert.Equal(("OK", true, true, false), (moving.CloseText, moving.CloseCommand.CanExecute(null), moving.MoveCommand.CanExecute(null), moving.IsStopping));
        Assert.Equal((0, (LibraryMove?)null), (_closes, moving.Result));
        Assert.Null(_settings.Stored.GameFor(Skyrim).Root);
    }

    [Fact]
    public async Task StoppedBeforeAnyFileMovedSaysNothingMoved()
    {
        _mover.Hold = new TaskCompletionSource();
        _mover.HoldAfter = 0;
        GameMoveViewModel moving = await PlannedAsync(Folder);
        List<bool> stopping = [];
        moving.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(GameMoveViewModel.IsStopping))
            {
                stopping.Add(moving.IsStopping);
            }
        };
        Task move = moving.MoveCommand.ExecuteAsync(null);

        moving.StopCommand.Execute(null);
        await move;

        Assert.Equal([true, false], stopping);
        Assert.Equal("Nothing moved, and Skyrim Special Edition stays where it was.", moving.Message);
    }

    [Fact]
    public async Task AFileThatCantMoveStopsItAndItCanBeTriedAgain()
    {
        _mover.MoveRefusal = new IOException("SkyUI.7z couldn't be moved: it's open in another program. Every file is back where it was.");
        GameMoveViewModel moving = await PlannedAsync(Folder);

        await moving.MoveCommand.ExecuteAsync(null);
        (GameMoveStage, string?) stopped = (moving.Stage, moving.Message);
        _mover.MoveRefusal = null;
        await moving.MoveCommand.ExecuteAsync(null);

        Assert.Equal((GameMoveStage.Stopped, "SkyUI.7z couldn't be moved: it's open in another program. Every file is back where it was."), stopped);
        Assert.Equal((GameMoveStage.Done, (string?)null, 1), (moving.Stage, moving.Message, _closes));
        Assert.Equal(Folder, _settings.Stored.GameFor(Skyrim).Root);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AFolderItsInAlreadyClosesWithNothingMoved()
    {
        _mover.Plan = plan => plan with { To = plan.From };

        GameMoveViewModel moving = await PlannedAsync(Folder);

        Assert.Equal((GameMoveStage.Done, 1, (LibraryMove?)null), (moving.Stage, _closes, moving.Result));
        Assert.Empty(_mover.Moves);
    }

    [Fact]
    public async Task ClosingItMovesNothing()
    {
        GameMoveViewModel moving = await PlannedAsync(Folder);

        moving.CloseCommand.Execute(null);

        Assert.Equal(1, _closes);
        Assert.Empty(_mover.Moves);
    }

    [Fact]
    public async Task ClosedWhileFindingWhatWouldMoveItSaysNothingMore()
    {
        _mover.PlanHold = new TaskCompletionSource();
        GameMoveViewModel moving = new(Skyrim, "Skyrim Special Edition", Folder, _mover, PickAsync, _uiThread, _logger);
        moving.CloseRequested += (_, _) => _closes++;
        moving.StartPlanning();

        moving.CloseCommand.Execute(null);
        _mover.PlanHold.SetResult();
        await moving.Planning;

        Assert.Equal((GameMoveStage.Planning, 1), (moving.Stage, _closes));
        Assert.Empty(moving.Facts);
    }

    [Fact]
    public void ADriveIsNamedWhereFoldersStartWithOne()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("E:", GameMoveViewModel.DriveOf(@"E:\Mods\Skyrim"));
            Assert.Equal(@"\\nas\games", GameMoveViewModel.DriveOf(@"\\nas\games\Skyrim"));
        }
        else
        {
            Assert.Null(GameMoveViewModel.DriveOf("/mnt/games/Skyrim"));
        }
    }

    private async Task<GameMoveViewModel> PlannedAsync(string? folder)
    {
        GameMoveViewModel moving = new(Skyrim, "Skyrim Special Edition", folder, _mover, PickAsync, _uiThread, _logger);
        moving.CloseRequested += (_, _) => _closes++;
        moving.StartPlanning();
        await moving.Planning;
        return moving;
    }

    private Task<string?> PickAsync(string title)
    {
        _pickers.Add(title);
        return Task.FromResult(_folders.TryDequeue(out string? folder) ? folder : null);
    }
}
