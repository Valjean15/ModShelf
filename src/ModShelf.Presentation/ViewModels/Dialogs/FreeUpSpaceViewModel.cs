using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Cleanup;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Frees up space in the library's folders (ADR 0104): lists what could go, for every game or one, asking the mods'
/// sites which files they still offer; shows it by group, the archived old versions and the duplicates chosen to go,
/// each file that may be the only copy left kept, saying why; then, once the user says so, deletes the chosen files, one
/// at a time, and says what it did.
/// </summary>
public sealed partial class FreeUpSpaceViewModel : ObservableObject
{
    private readonly ILibraryCleanup _cleanup;
    private readonly GameRef? _game;
    private readonly ISourceRegistry _sources;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private CancellationTokenSource? _working;
    private int _selectedCount;
    private long _selectedBytes;

    /// <param name="game">The game whose files to look at; null for every game's.</param>
    /// <param name="gameName">The game's name, for the title; null for every game.</param>
    public FreeUpSpaceViewModel(ILibraryCleanup cleanup, GameRef? game, string? gameName, ISourceRegistry sources, IDialogService dialogs, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _cleanup = cleanup;
        _game = game;
        _sources = sources;
        _dialogs = dialogs;
        _logger = logger;
        Title = gameName is null ? Strings.CleanupTitle : Text.Format(Strings.CleanupTitleGame, gameName);
    }

    /// <summary>
    /// Asks the dialog showing it to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing), nameof(IsListed), nameof(IsDeleting), nameof(IsWorking), nameof(IsDone), nameof(CanClose), nameof(CloseText), nameof(ShowsRows))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(StopCommand), nameof(CloseCommand))]
    public partial FreeUpSpaceStage Stage { get; private set; }

    public bool IsListing => Stage == FreeUpSpaceStage.Listing;

    public bool IsListed => Stage == FreeUpSpaceStage.Listed;

    public bool IsDeleting => Stage == FreeUpSpaceStage.Deleting;

    /// <summary>
    /// Whether it lists or deletes, which Stop ends.
    /// </summary>
    public bool IsWorking => IsListing || IsDeleting;

    public bool IsDone => Stage == FreeUpSpaceStage.Done;

    /// <summary>
    /// Whether the dialog may close: not while it works, which Stop ends first.
    /// </summary>
    public bool CanClose => !IsWorking;

    public string CloseText => IsDone ? Strings.DialogOk : Strings.DialogCancel;

    /// <summary>
    /// Whether the files show: once listed, while there are any.
    /// </summary>
    public bool ShowsRows => IsListed && Rows.Count > 0;

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// How much can go, and how many files are kept; or that nothing can.
    /// </summary>
    [ObservableProperty]
    public partial string? SummaryText { get; private set; }

    /// <summary>
    /// Each group's heading, then its files, in the order they're shown.
    /// </summary>
    public ObservableCollection<ObservableObject> Rows { get; } = [];

    /// <summary>
    /// The groups, in the order they're shown.
    /// </summary>
    public ObservableCollection<CleanupGroupRow> Groups { get; } = [];

    /// <summary>
    /// The button that deletes the chosen files, with their size.
    /// </summary>
    [ObservableProperty]
    public partial string DeleteText { get; private set; } = Strings.CleanupDeleteNone;

    /// <summary>
    /// What deleting did, once it's done; or why there's nothing to show.
    /// </summary>
    [ObservableProperty]
    public partial string? ResultText { get; private set; }

    /// <summary>
    /// The files not deleted, each in a sentence.
    /// </summary>
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    public partial bool HasProblems { get; private set; }

    /// <summary>
    /// What could go; null until it's listed.
    /// </summary>
    public CleanupPlan? Plan { get; private set; }

    /// <summary>
    /// What deleting did; null until it's done, or when it stopped or failed.
    /// </summary>
    public CleanupResult? Result { get; private set; }

    /// <summary>
    /// The listing, once started.
    /// </summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    private bool CanDelete => IsListed && _selectedCount > 0;

    /// <summary>
    /// Starts listing what could go, as the dialog opens.
    /// </summary>
    public void StartListing() => Listing = ListAsync();

    /// <summary>
    /// Why a file is kept, or what it's a copy of, in a sentence; null when there's nothing to say.
    /// </summary>
    internal static string? Said(CleanupEntry entry, string site)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kept switch
        {
            null when entry.SameAs is { } copy => Text.Format(Strings.CleanupCopyOf, copy.File.RelativePath, copy.ModName),
            null => null,
            KeptBecause.ModGone => Text.Format(Strings.CleanupKeptGone, site),
            KeptBecause.NotOffered => Text.Format(Strings.CleanupKeptNotOffered, site),
            KeptBecause.MatchesSiteFile => Text.Format(Strings.CleanupKeptMatches, entry.KeptDetail, site),
            _ => Text.Format(Strings.CleanupKeptNotChecked, site, entry.KeptDetail),
        };
    }

    private async Task ListAsync()
    {
        using CancellationTokenSource working = new();
        _working = working;
        Stage = FreeUpSpaceStage.Listing;
        ShowProgress(Strings.CleanupListing, 0, 0);
        try
        {
            CleanupPlan plan = await _cleanup.PlanAsync(
                _game, new Progress<CleanupProgress>(progress => ShowProgress(Strings.CleanupAsking, progress.Done, progress.Total)), working.Token);
            Plan = plan;
            Show(plan);
            Stage = FreeUpSpaceStage.Listed;
        }
        catch (OperationCanceledException) when (working.IsCancellationRequested)
        {
            ResultText = Strings.CleanupListStopped;
            Stage = FreeUpSpaceStage.Done;
        }
        catch (InvalidDataException exception)
        {
            ResultText = exception.Message;
            Stage = FreeUpSpaceStage.Done;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogListFailed(_logger, exception);
            ResultText = Strings.CleanupFailed;
            Stage = FreeUpSpaceStage.Done;
        }
        finally
        {
            _working = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        CleanupEntry[] chosen = [.. Groups.SelectMany(group => group.Files).Where(file => file.IsSelected).Select(file => file.Entry)];
        long size = chosen.Sum(entry => entry.SizeInBytes);
        string message = FilesAndSize(chosen.Length, size, string.Empty, Strings.CleanupConfirmOne, Strings.CleanupConfirm);
        if (!await _dialogs.ConfirmAsync(Strings.CleanupConfirmTitle, message, Strings.CleanupConfirmDelete, CancellationToken.None))
        {
            return;
        }

        using CancellationTokenSource working = new();
        _working = working;
        Stage = FreeUpSpaceStage.Deleting;
        ShowProgress(Strings.CleanupDeleting, 0, chosen.Length);
        try
        {
            CleanupResult result = await _cleanup.DeleteAsync(
                chosen, new Progress<CleanupProgress>(progress => ShowProgress(Strings.CleanupDeleting, progress.Done, progress.Total)), working.Token);
            Result = result;
            ResultText = FilesAndSize(result.Deleted.Count, result.Freed, Strings.CleanupDeletedNone, Strings.CleanupDeletedOne, Strings.CleanupDeleted);

            foreach (CleanupFailure failure in result.Failures)
            {
                Problems.Add(Text.Format(Strings.LibrarySelectedProblem, failure.Entry.FileName, failure.Message));
            }

            HasProblems = Problems.Count > 0;
        }
        catch (OperationCanceledException) when (working.IsCancellationRequested)
        {
            ResultText = Strings.CleanupDeleteStopped;
        }
        catch (InvalidOperationException exception)
        {
            // The library couldn't be held: the message says to try again later.
            ResultText = exception.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeleteFailed(_logger, exception);
            ResultText = Strings.CleanupFailed;
        }
        finally
        {
            _working = null;
            Stage = FreeUpSpaceStage.Done;
        }
    }

    [RelayCommand(CanExecute = nameof(IsWorking))]
    private void Stop() => _working?.Cancel();

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _working?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows what could go: each group with files, its heading, then its files; the archived old versions and the
    /// duplicates chosen to go.
    /// </summary>
    private void Show(CleanupPlan plan)
    {
        foreach (IGrouping<CleanupGroup, CleanupEntry> entries in plan.Entries.GroupBy(entry => entry.Group).OrderBy(group => group.Key))
        {
            (string heading, string note) = entries.Key switch
            {
                CleanupGroup.Archived => (Strings.CleanupArchived, Strings.CleanupArchivedNote),
                CleanupGroup.Duplicates => (Strings.CleanupDuplicates, Strings.CleanupDuplicatesNote),
                CleanupGroup.Unwatched => (Strings.CleanupUnwatched, Strings.CleanupUnwatchedNote),
                _ => (Strings.CleanupUnmatched, Strings.CleanupUnmatchedNote),
            };
            CleanupGroupRow group = new(entries.Key, heading, note);
            bool chosen = entries.Key is CleanupGroup.Archived or CleanupGroup.Duplicates;
            Rows.Add(group);
            foreach (CleanupEntry entry in entries)
            {
                CleanupFileRow file = new(entry, Said(entry, _sources.SiteName(entry.Mod.Game.Source)), chosen, Selected);
                group.Add(file);
                Rows.Add(file);
                if (file.IsSelected)
                {
                    _selectedCount++;
                    _selectedBytes += entry.SizeInBytes;
                }
            }

            int canGo = entries.Count(entry => entry.CanGo);
            int kept = entries.Count() - canGo;
            string total = FilesAndSize(
                canGo, entries.Where(entry => entry.CanGo).Sum(entry => entry.SizeInBytes), Strings.CleanupGroupNone, Strings.CleanupGroupOne, Strings.CleanupGroup);
            group.TotalText = kept == 0 ? total : string.Join(' ', total, Counts.Of(kept, Strings.CleanupGroupKeptOne, Strings.CleanupGroupKept));
            Groups.Add(group);
        }

        int allCanGo = plan.Entries.Count(entry => entry.CanGo);
        int allKept = plan.Entries.Count - allCanGo;
        SummaryText = plan.Entries.Count == 0
            ? Strings.CleanupNothing
            : string.Join(
                ' ',
                FilesAndSize(allCanGo, plan.CanFree, Strings.CleanupCanGoNone, Strings.CleanupCanGoOne, Strings.CleanupCanGo),
                allKept == 0 ? string.Empty : Counts.Of(allKept, Strings.CleanupKeptOne, Strings.CleanupKept)).TrimEnd();
        ShowSelection();
        OnPropertyChanged(nameof(ShowsRows));
    }

    /// <summary>
    /// A count of files and their size, in the wording for none, one or more: <paramref name="one"/> and
    /// <paramref name="many"/> take the count as {0} and the size as {1}.
    /// </summary>
    private static string FilesAndSize(int count, long bytes, string none, string one, string many)
        => count == 0 ? none : Text.Format(count == 1 ? one : many, count, Sizes.Format(bytes));

    private void Selected(CleanupFileRow file)
    {
        int sign = file.IsSelected ? 1 : -1;
        _selectedCount += sign;
        _selectedBytes += sign * file.Entry.SizeInBytes;
        Groups.FirstOrDefault(group => group.Group == file.Entry.Group)?.SelectionChanged();
        ShowSelection();
    }

    private void ShowSelection()
    {
        DeleteText = _selectedCount == 0 ? Strings.CleanupDeleteNone : Text.Format(Strings.CleanupDelete, Sizes.Format(_selectedBytes));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void ShowProgress(string format, int done, int total)
    {
        ProgressPercent = total == 0 ? 0 : 100.0 * done / total;
        ProgressText = Text.Format(format, done, total);
    }
}
