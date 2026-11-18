using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Archives;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Imports a folder's archives into the library (ADR 0101): how many there are and the requests identifying them takes,
/// asking first when the site's requests left look too few; then what each is, and what importing would copy; then the
/// copying, an archive at a time, and what it did. The archives are only ever read.
/// </summary>
public sealed partial class ImportArchivesViewModel : ObservableObject
{
    private readonly IArchiveImport _archives;
    private readonly ArchiveScan _scan;
    private readonly string _site;
    private readonly ISourceRegistry _sources;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private CancellationTokenSource? _working;
    private int _total;

    /// <param name="gameName">The game's name, for the title.</param>
    public ImportArchivesViewModel(
        IArchiveImport archives,
        ArchiveScan scan,
        string gameName,
        ISourceRegistry sources,
        IDialogService dialogs,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _archives = archives;
        _scan = scan;
        _sources = sources;
        _dialogs = dialogs;
        _timeProvider = timeProvider;
        _logger = logger;
        _site = sources.SiteName(scan.Game.Source);
        Title = Text.Format(Strings.ArchiveImportTitle, gameName);
        ScanText = string.Join(
            ' ',
            Text.Format(scan.Archives.Count == 1 ? Strings.ArchiveScannedOne : Strings.ArchiveScanned, scan.Archives.Count, scan.Folder),
            Text.Format(
                scan.Requests switch
                {
                    0 => Strings.ArchiveRequestsNone,
                    1 => Strings.ArchiveRequestsOne,
                    _ => Strings.ArchiveRequests,
                },
                scan.Requests,
                _site));
        LookUpText = scan.CanLookUp ? null : Text.Format(Strings.ArchiveCantLookUp, _site);
    }

    /// <summary>
    /// Asks the dialog showing it to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string Title { get; }

    /// <summary>
    /// How many archives there are, and the requests identifying them takes.
    /// </summary>
    public string ScanText { get; }

    /// <summary>
    /// That the site can't find archives by content, so only those noted or named can be identified; null when it can.
    /// </summary>
    public string? LookUpText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanned), nameof(ShowsLookUpText), nameof(IsIdentified), nameof(IsWorking), nameof(IsDone), nameof(CanClose), nameof(CloseText))]
    [NotifyCanExecuteChangedFor(nameof(IdentifyCommand), nameof(ImportCommand), nameof(StopCommand), nameof(CloseCommand))]
    public partial ImportArchivesStage Stage { get; private set; }

    public bool IsScanned => Stage == ImportArchivesStage.Scanned;

    public bool ShowsLookUpText => IsScanned && LookUpText is not null;

    public bool IsIdentified => Stage == ImportArchivesStage.Identified;

    /// <summary>
    /// Whether the archives are being identified or copied, which Stop ends.
    /// </summary>
    public bool IsWorking => Stage is ImportArchivesStage.Identifying or ImportArchivesStage.Importing;

    public bool IsDone => Stage == ImportArchivesStage.Done;

    /// <summary>
    /// Whether the dialog may close: not while it works, which Stop ends first.
    /// </summary>
    public bool CanClose => !IsWorking;

    public string CloseText => IsDone ? Strings.DialogOk : Strings.DialogCancel;

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// Each archive as identified.
    /// </summary>
    public ObservableCollection<ImportArchiveRow> Rows { get; } = [];

    /// <summary>
    /// What importing would do: the archives it copies, and those it leaves alone, by why.
    /// </summary>
    public ObservableCollection<ImportListFact> Facts { get; } = [];

    /// <summary>
    /// What the import did, once it's done.
    /// </summary>
    [ObservableProperty]
    public partial string? ResultText { get; private set; }

    /// <summary>
    /// What it couldn't copy, each in a sentence.
    /// </summary>
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    public partial bool HasProblems { get; private set; }

    /// <summary>
    /// The archives as identified; null until they are.
    /// </summary>
    public ArchivePlan? Plan { get; private set; }

    /// <summary>
    /// What the import did; null until it's done, or when it stopped or failed.
    /// </summary>
    public ArchiveImportResult? Result { get; private set; }

    private bool CanImport => IsIdentified && Plan is { ToImport.Count: > 0 };

    /// <summary>
    /// What an archive is, or why it's left alone, in a sentence.
    /// </summary>
    internal static string Said(PlannedArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        return archive.State switch
        {
            ArchiveState.Identified => Text.Format(
                archive.By switch
                {
                    ArchiveClue.Notes => Strings.ArchiveByNotes,
                    ArchiveClue.Name => Strings.ArchiveByName,
                    _ => Strings.ArchiveByHash,
                },
                archive.ModName,
                archive.FileName),
            ArchiveState.InLibrary => archive.ModName is null ? Strings.ArchiveInLibrary : Text.Format(Strings.ArchiveInLibraryAs, archive.ModName, archive.FileName),
            ArchiveState.Duplicate => archive.Why ?? string.Empty,
            _ => Text.Format(Strings.ArchiveUnknown, archive.Why ?? Strings.SomethingWentWrong),
        };
    }

    [RelayCommand(CanExecute = nameof(IsScanned))]
    private async Task IdentifyAsync()
    {
        if (!await QuotaChecks.GoOnAsync(_sources, _dialogs, _timeProvider, [(_scan.Game.Source, _scan.Requests)], CancellationToken.None))
        {
            return;
        }

        using CancellationTokenSource working = new();
        _working = working;
        Stage = ImportArchivesStage.Identifying;
        _total = _scan.Archives.Count;
        ShowProgress(Strings.ArchiveIdentifying, 0);
        try
        {
            ArchivePlan plan = await _archives.IdentifyAsync(_scan, new Progress<int>(done => ShowProgress(Strings.ArchiveIdentifying, done)), working.Token);
            Plan = plan;
            foreach (PlannedArchive archive in plan.Archives)
            {
                Rows.Add(new ImportArchiveRow(archive.Name, Said(archive), archive.State == ArchiveState.Identified));
            }

            ShowFacts(plan);
            Stage = ImportArchivesStage.Identified;
        }
        catch (OperationCanceledException) when (working.IsCancellationRequested)
        {
            ResultText = Strings.ArchiveIdentifyStopped;
            Stage = ImportArchivesStage.Done;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogIdentifyFailed(_logger, exception);
            ResultText = Strings.ArchiveImportFailed;
            Stage = ImportArchivesStage.Done;
        }
        finally
        {
            _working = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        using CancellationTokenSource working = new();
        _working = working;
        Stage = ImportArchivesStage.Importing;
        _total = Plan!.ToImport.Count;
        ShowProgress(Strings.ArchiveCopying, 0);
        try
        {
            ArchiveImportResult result = await _archives.ImportAsync(Plan, new Progress<int>(done => ShowProgress(Strings.ArchiveCopying, done)), working.Token);
            Result = result;
            ResultText = Summary(result);
            foreach (ArchiveProblem problem in result.Problems)
            {
                Problems.Add(Text.Format(Strings.LibrarySelectedProblem, problem.Name, problem.Message));
            }

            HasProblems = Problems.Count > 0;
        }
        catch (OperationCanceledException) when (working.IsCancellationRequested)
        {
            ResultText = Strings.ArchiveImportStopped;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogImportFailed(_logger, exception);
            ResultText = Strings.ArchiveImportFailed;
        }
        finally
        {
            _working = null;
            Stage = ImportArchivesStage.Done;
        }
    }

    [RelayCommand(CanExecute = nameof(IsWorking))]
    private void Stop() => _working?.Cancel();

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static string Summary(ArchiveImportResult result)
    {
        List<string> said = [Counts.Of(result.Imported.Count, Strings.ArchiveImportedOne, Strings.ArchiveImported)];
        if (result.ModsAdded > 0)
        {
            said.Add(Counts.Of(result.ModsAdded, Strings.ArchiveWatchedOne, Strings.ArchiveWatched));
        }

        if (result.InLibrary > 0)
        {
            said.Add(Counts.Of(result.InLibrary, Strings.ArchiveAlreadyOne, Strings.ArchiveAlready));
        }

        return string.Join(' ', said);
    }

    private void ShowFacts(ArchivePlan plan)
    {
        int toCopy = plan.ToImport.Count;
        string copied = Counts.Of(toCopy, Strings.ArchiveToCopyNone, Strings.ArchiveToCopyOne, Strings.ArchiveToCopy);
        if (plan.NewMods > 0)
        {
            copied += Counts.Of(plan.NewMods, Strings.ArchiveNewModsOne, Strings.ArchiveNewMods);
        }

        Facts.Add(new ImportListFact(Strings.ArchiveToCopyLabel, copied));
        foreach ((ArchiveState state, string label) in new[]
        {
            (ArchiveState.InLibrary, Strings.ArchiveInLibraryLabel),
            (ArchiveState.Duplicate, Strings.ArchiveDuplicatesLabel),
            (ArchiveState.Unknown, Strings.ArchiveUnknownLabel),
        })
        {
            int count = plan.Count(state);
            if (count > 0)
            {
                Facts.Add(new ImportListFact(label, Text.Format(Strings.ArchiveCount, count)));
            }
        }

        ImportCommand.NotifyCanExecuteChanged();
    }

    private void ShowProgress(string format, int done)
    {
        ProgressPercent = 100.0 * done / Math.Max(1, _total);
        ProgressText = Text.Format(format, done, _total);
    }
}
