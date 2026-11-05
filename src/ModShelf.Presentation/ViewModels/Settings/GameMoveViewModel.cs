using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The dialog that moves a game's files to another folder: what would move, from where to where, the room there, the
/// files there already and those missing, then <b>Move</b>, with how far it is and <b>Stop</b>. A folder without room
/// enough, or one the files can't go to, is refused, and another can be chosen. A move that stops puts back every file
/// it moved, and says so.
/// </summary>
public sealed partial class GameMoveViewModel : ObservableObject
{
    private readonly ILibraryMover _mover;
    private readonly Func<string, Task<string?>> _pickFolder;
    private readonly IUiThread _uiThread;
    private readonly ILogger _logger;
    private CancellationTokenSource? _stopping;
    private GameMovePlan? _plan;
    private string? _folder;
    private int _moved;
    private bool _closed;

    /// <param name="name">The game's name.</param>
    /// <param name="folder">The game's new folder; null for the library's.</param>
    /// <param name="pickFolder">Asks the user for another folder, with the title given; null when they don't choose one.</param>
    internal GameMoveViewModel(
        GameRef game,
        string name,
        string? folder,
        ILibraryMover mover,
        Func<string, Task<string?>> pickFolder,
        IUiThread uiThread,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(pickFolder);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        Game = game;
        Name = name;
        _folder = folder;
        _mover = mover;
        _pickFolder = pickFolder;
        _uiThread = uiThread;
        _logger = logger;
        Title = Text.Format(Strings.GameMoveAskTitle, name);
        StopNote = Text.Format(Strings.GameMoveStopNote, name);
    }

    /// <summary>
    /// Asks to close the dialog: the files moved, there was nothing to move, or the user closed it.
    /// </summary>
    public event EventHandler? CloseRequested;

    public GameRef Game { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanning), nameof(IsAsking), nameof(OffersAnotherFolder), nameof(IsMoving), nameof(IsStopped))]
    [NotifyPropertyChangedFor(nameof(CanClose), nameof(CloseText), nameof(ShowsStopNote))]
    [NotifyCanExecuteChangedFor(nameof(MoveCommand), nameof(ChooseAnotherCommand), nameof(StopCommand), nameof(CloseCommand))]
    public partial GameMoveStage Stage { get; private set; }

    public bool IsPlanning => Stage == GameMoveStage.Planning;

    public bool IsAsking => Stage == GameMoveStage.Asking;

    /// <summary>
    /// Whether the files can't go to the folder chosen, for want of room or otherwise, so another can be chosen.
    /// </summary>
    public bool OffersAnotherFolder => Stage is GameMoveStage.NoRoom or GameMoveStage.Refused;

    public bool IsMoving => Stage == GameMoveStage.Moving;

    public bool IsStopped => Stage == GameMoveStage.Stopped;

    /// <summary>
    /// Whether the dialog can close: not while the files move, which Stop ends first.
    /// </summary>
    public bool CanClose => Stage != GameMoveStage.Moving;

    /// <summary>
    /// "Cancel", or "OK" once the move stopped.
    /// </summary>
    public string CloseText => IsStopped ? Strings.DialogOk : Strings.DialogCancel;

    /// <summary>
    /// Whether to say what stopping does: while asking, and while the files move.
    /// </summary>
    public bool ShowsStopNote => IsAsking || IsMoving;

    /// <summary>
    /// Such as "Move Dragon's Dogma 2's files?", "There isn't room on E:" or "The move stopped".
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; }

    /// <summary>
    /// Why the files can't go where they were to, or why the move stopped; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// What the move would do: from where, to where, what moves, the room there, the files there already and those
    /// missing.
    /// </summary>
    public ObservableCollection<GameMoveFact> Facts { get; } = [];

    /// <summary>
    /// Such as "Stopping puts back every file moved, and Dragon's Dogma 2 stays where it was."
    /// </summary>
    public string StopNote { get; }

    /// <summary>
    /// Such as "136 of 211 files · 11.7 GB of 18.2 GB".
    /// </summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    /// <summary>
    /// How far the move is, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// Whether Stop was pressed, and the files moved are going back.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsStopping { get; private set; }

    /// <summary>
    /// What the move did, once the files moved; null until then, and when there was nothing to move.
    /// </summary>
    public LibraryMove? Result { get; private set; }

    /// <summary>
    /// Finding what would move, for tests to wait on.
    /// </summary>
    internal Task Planning { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The drive a folder is on, as people name it, such as "E:"; null where folders don't start with one, as on Linux
    /// and macOS.
    /// </summary>
    internal static string? DriveOf(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        string root = (Path.GetPathRoot(folder) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return root.Length > 0 ? root : null;
    }

    /// <summary>
    /// Starts finding what moving the files to the folder chosen would do.
    /// </summary>
    internal void StartPlanning() => Planning = PlanAsync(_folder);

    private static string MessageOf(Exception exception)
        => exception is ArgumentException refused ? refused.MessageForUser() : exception.Message;

    private bool CanMove() => (IsAsking || IsStopped) && _plan is not null;

    private bool CanStop() => IsMoving && !IsStopping;

    /// <summary>
    /// Moves the files, as the user confirms, or tries again once a move stopped.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMove))]
    private async Task MoveAsync()
    {
        GameMovePlan plan = _plan!;
        using CancellationTokenSource stopping = new();
        _stopping = stopping;
        Interlocked.Exchange(ref _moved, 0);
        Message = null;
        Title = Text.Format(Strings.GameMoveRunTitle, Name);
        ShowProgress(new LibraryMoveProgress(0, plan.Files, 0, plan.Bytes));
        Stage = GameMoveStage.Moving;
        try
        {
            LibraryMove move = await _mover.MoveGameAsync(Game, _folder, new UiProgress(this), stopping.Token);
            Finish(move);
        }
        catch (OperationCanceledException exception) when (stopping.IsCancellationRequested)
        {
            // Once a file moved, the mover says where they all are.
            ShowStopped(Volatile.Read(ref _moved) > 0 ? exception.Message : Text.Format(Strings.GameMoveStoppedNothing, Name));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
        {
            // Such as a file open in another program, or a download of the game started meanwhile.
            ShowStopped(MessageOf(exception));
        }
        catch (Exception exception)
        {
            LogMoveFailed(_logger, exception, Game.ToString());
            ShowStopped(Strings.SomethingWentWrong);
        }
        finally
        {
            _stopping = null;
            IsStopping = false;
        }
    }

    /// <summary>
    /// Asks for another folder, once the one chosen was refused, and finds what moving the files there would do.
    /// </summary>
    [RelayCommand(CanExecute = nameof(OffersAnotherFolder))]
    private async Task ChooseAnotherAsync()
    {
        if (await _pickFolder(Text.Format(Strings.GameFolderChooseTitle, Name)) is { } folder)
        {
            Planning = PlanAsync(folder);
            await Planning;
        }
    }

    /// <summary>
    /// Stops the move: the files moved go back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsStopping = true;
        _stopping?.Cancel();
    }

    /// <summary>
    /// Closes the dialog, moving nothing more.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _closed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finds what moving the files to <paramref name="folder"/> would do: says it and asks, or says why they can't go
    /// there.
    /// </summary>
    /// <param name="folder">The game's new folder; null for the library's.</param>
    private async Task PlanAsync(string? folder)
    {
        Message = null;
        Title = Text.Format(Strings.GameMoveAskTitle, Name);
        Stage = GameMoveStage.Planning;
        try
        {
            GameMovePlan plan = await _mover.PlanGameMoveAsync(Game, folder, CancellationToken.None);
            if (_closed)
            {
                // The dialog closed meanwhile.
                return;
            }

            _plan = plan;
            _folder = folder;
            if (string.Equals(plan.From, plan.To, StringComparison.Ordinal))
            {
                // The game's files are there already.
                Finish(null);
            }
            else if (!plan.Fits)
            {
                ShowNoRoom(plan);
            }
            else
            {
                ShowFacts(plan);
                Stage = GameMoveStage.Asking;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
        {
            // Such as a folder inside another game's, or downloads of the game that aren't finished.
            Refuse(MessageOf(exception));
        }
        catch (Exception exception)
        {
            LogMoveFailed(_logger, exception, Game.ToString());
            Refuse(Strings.SomethingWentWrong);
        }
    }

    private void ShowFacts(GameMovePlan plan)
    {
        string size = Sizes.Format(plan.Bytes);
        Facts.Add(new GameMoveFact(Strings.GameMoveFrom, plan.From));
        Facts.Add(new GameMoveFact(Strings.GameMoveTo, plan.To));
        Facts.Add(new GameMoveFact(
            Strings.GameMoveMoving,
            plan.Files switch
            {
                0 => Strings.GameMoveMovingNone,
                1 => Text.Format(Strings.GameMoveMovingOne, size),
                _ => Text.Format(Strings.GameMoveMovingMany, plan.Files, size),
            }));

        // On the same drive, files move without taking room.
        if (!plan.SameDrive && plan.FreeSpace is { } free)
        {
            string label = DriveOf(plan.To) is { } drive ? Text.Format(Strings.GameMoveRoomOn, drive) : Strings.GameMoveRoom;
            Facts.Add(new GameMoveFact(label, Text.Format(Strings.GameMoveFree, Sizes.Format(free))));
        }

        if (plan.AlreadyThere > 0)
        {
            string there = plan.AlreadyThere == 1
                ? Strings.GameMoveAlreadyThereOne
                : Text.Format(Strings.GameMoveAlreadyThereMany, plan.AlreadyThere);
            Facts.Add(new GameMoveFact(
                Strings.GameMoveAlreadyThere,
                plan.CopiesKept switch
                {
                    0 => there,
                    1 => Text.Format(Strings.GameMoveCopyKept, there),
                    _ => Text.Format(Strings.GameMoveCopiesKept, there, plan.CopiesKept),
                }));
        }

        if (plan.Missing > 0)
        {
            Facts.Add(new GameMoveFact(
                Strings.GameMoveMissing,
                plan.Missing == 1 ? Strings.GameMoveMissingOne : Text.Format(Strings.GameMoveMissingMany, plan.Missing)));
        }
    }

    private void ShowNoRoom(GameMovePlan plan)
    {
        string need = Sizes.Format(plan.Bytes);
        string free = Sizes.Format(plan.FreeSpace ?? 0);
        if (DriveOf(plan.To) is { } drive)
        {
            Title = Text.Format(Strings.GameMoveNoRoomTitleOn, drive);
            Message = Text.Format(Strings.GameMoveNoRoomOn, Name, need, drive, free);
        }
        else
        {
            Title = Strings.GameMoveNoRoomTitle;
            Message = Text.Format(Strings.GameMoveNoRoom, Name, need, free);
        }

        Stage = GameMoveStage.NoRoom;
    }

    private void Refuse(string message)
    {
        Title = Text.Format(Strings.GameMoveRefusedTitle, Name);
        Message = message;
        Stage = GameMoveStage.Refused;
    }

    private void ShowStopped(string message)
    {
        Title = Strings.GameMoveStoppedTitle;
        Message = message;
        Stage = GameMoveStage.Stopped;
    }

    private void ShowProgress(LibraryMoveProgress progress)
    {
        ProgressText = Text.Format(
            Strings.GameMoveProgress,
            progress.Moved,
            progress.Files,
            Sizes.Format(progress.MovedBytes),
            Sizes.Format(progress.Bytes));
        double done = progress.Bytes > 0
            ? (double)progress.MovedBytes / progress.Bytes
            : progress.Files > 0 ? (double)progress.Moved / progress.Files : 0;
        ProgressPercent = Math.Clamp(100 * done, 0, 100);
    }

    private void Finish(LibraryMove? move)
    {
        Result = move;
        Stage = GameMoveStage.Done;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// How far the move is, told from wherever it runs and shown on the UI thread.
    /// </summary>
    private sealed class UiProgress(GameMoveViewModel moving) : IProgress<LibraryMoveProgress>
    {
        public void Report(LibraryMoveProgress value)
        {
            Interlocked.Exchange(ref moving._moved, value.Moved);
            moving._uiThread.Post(() => moving.ShowProgress(value));
        }
    }
}
