using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Lists;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Imports a list, or a site's collection read as one, into the library (ADR 0098): what importing it would do, worked
/// out from the library alone, with the choice to download its files or only watch its mods, and what a collection needs
/// from elsewhere, which isn't downloaded; then the import, a mod at a time, and what it did. It warns first when a
/// site's API requests left look too few, as Update all does.
/// </summary>
public sealed partial class ImportListViewModel : ObservableObject
{
    private readonly IModLists _lists;
    private readonly ListImportPlan _plan;
    private readonly ISourceRegistry _sources;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private CancellationTokenSource? _importing;

    public ImportListViewModel(
        IModLists lists,
        ListImportPlan plan,
        ISourceRegistry sources,
        IDialogService dialogs,
        IBrowserLauncher browser,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _lists = lists;
        _plan = plan;
        _sources = sources;
        _dialogs = dialogs;
        _timeProvider = timeProvider;
        _logger = logger;
        Title = plan.List.Title is { } title ? Text.Format(Strings.ListImportTitleNamed, title) : Strings.ListImportTitle;
        OptionalText = Counts.Of(plan.OptionalFiles, Strings.ListImportOptionalOne, Strings.ListImportOptional);
        Elsewhere = [.. plan.List.Elsewhere.Select(thing => new ImportListElsewhereViewModel(thing, browser, dialogs))];
        ShowFacts();
    }

    /// <summary>
    /// Asks the dialog showing it to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string Title { get; }

    /// <summary>
    /// What importing would do: the list's mods, and the files it would download as the choices are.
    /// </summary>
    public ObservableCollection<ImportListFact> Facts { get; } = [];

    /// <summary>
    /// Whether the listed files are downloaded, rather than the mods only watched; the default.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchOnly))]
    public partial bool DownloadFiles { get; set; } = true;

    /// <summary>
    /// Whether the mods are only watched, nothing downloaded: the other choice to <see cref="DownloadFiles"/>.
    /// </summary>
    public bool WatchOnly
    {
        get => !DownloadFiles;
        set => DownloadFiles = !value;
    }

    /// <summary>
    /// Whether the files the list marks optional are downloaded too.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeOptional { get; set; }

    /// <summary>
    /// Whether a listed file its site no longer offers is replaced by the mod's newest main file.
    /// </summary>
    [ObservableProperty]
    public partial bool NewestForMissing { get; set; }

    /// <summary>
    /// Whether the list marks files optional, which the option to download them too says.
    /// </summary>
    public bool HasOptional => _plan.OptionalFiles > 0;

    public string OptionalText { get; }

    /// <summary>
    /// What a collection needs from outside its mods' site, which isn't downloaded; none for a list file.
    /// </summary>
    public IReadOnlyList<ImportListElsewhereViewModel> Elsewhere { get; }

    public bool HasElsewhere => Elsewhere.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(IsImporting), nameof(IsDone), nameof(CanClose), nameof(CloseText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(StopCommand), nameof(CloseCommand))]
    public partial ImportListStage Stage { get; private set; }

    public bool IsAsking => Stage == ImportListStage.Asking;

    public bool IsImporting => Stage == ImportListStage.Importing;

    public bool IsDone => Stage == ImportListStage.Done;

    /// <summary>
    /// Whether the dialog may close: not while the list is imported, which Stop ends first.
    /// </summary>
    public bool CanClose => !IsImporting;

    public string CloseText => IsDone ? Strings.DialogOk : Strings.DialogCancel;

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// What the import did, once it's done.
    /// </summary>
    [ObservableProperty]
    public partial string? ResultText { get; private set; }

    /// <summary>
    /// What it couldn't do, each in a sentence.
    /// </summary>
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    public partial bool HasProblems { get; private set; }

    /// <summary>
    /// What it did, once it's done; null until then, or when it stopped or failed.
    /// </summary>
    public ListImportResult? Result { get; private set; }

    private ListImportOptions Options => new(DownloadFiles, IncludeOptional, NewestForMissing);

    private bool CanImport => IsAsking && (_plan.NewMods > 0 || (DownloadFiles && _plan.FilesToDownload(IncludeOptional) > 0));

    /// <summary>
    /// What a mod or file that couldn't be imported says, in a sentence.
    /// </summary>
    internal static string Said(ListImportProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return problem.Kind switch
        {
            ListImportProblemKind.NoSite => Text.Format(Strings.ListProblemNoSite, problem.ModName, problem.Mod.Game.Source.Value),
            ListImportProblemKind.SiteFailed => Text.Format(Strings.ListProblemSiteFailed, problem.ModName, problem.Detail).TrimEnd(),
            ListImportProblemKind.FileGone => Text.Format(Strings.ListProblemFileGone, problem.ModName, problem.File?.Id),
            _ => Text.Format(Strings.ListProblemNotDone, problem.ModName, problem.Detail ?? Strings.SomethingWentWrong),
        };
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        ListImportOptions options = Options;
        if (!await QuotaChecks.GoOnAsync(_sources, _dialogs, _timeProvider, Needs(options), CancellationToken.None))
        {
            return;
        }

        using CancellationTokenSource importing = new();
        _importing = importing;
        Stage = ImportListStage.Importing;
        ShowProgress(0);
        try
        {
            ListImportResult result = await _lists.ImportAsync(_plan, options, new Progress<int>(ShowProgress), null, importing.Token);
            Result = result;
            ResultText = Summary(result);
            foreach (ListImportProblem problem in result.Problems)
            {
                Problems.Add(Said(problem));
            }

            HasProblems = Problems.Count > 0;
        }
        catch (OperationCanceledException) when (importing.IsCancellationRequested)
        {
            ResultText = Strings.ListImportStopped;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogImportFailed(_logger, exception);
            ResultText = Strings.ListImportFailed;
        }
        finally
        {
            _importing = null;
            Stage = ImportListStage.Done;
        }
    }

    [RelayCommand(CanExecute = nameof(IsImporting))]
    private void Stop() => _importing?.Cancel();

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnDownloadFilesChanged(bool value) => ShowFacts();

    partial void OnIncludeOptionalChanged(bool value) => ShowFacts();

    private static string Summary(ListImportResult result)
    {
        List<string> said = [];
        if (result.ModsAdded > 0)
        {
            said.Add(Counts.Of(result.ModsAdded, Strings.ListImportAddedOne, Strings.ListImportAdded));
        }

        if (result.FilesQueued > 0)
        {
            said.Add(Counts.Of(result.FilesQueued, Strings.ListImportQueuedOne, Strings.ListImportQueued));
        }

        if (result.FilesInLibrary > 0)
        {
            said.Add(Counts.Of(result.FilesInLibrary, Strings.ListImportInLibraryOne, Strings.ListImportInLibrary));
        }

        return said.Count == 0 ? Strings.ListImportNothing : string.Join(' ', said);
    }

    /// <summary>
    /// The requests importing takes from each site: one to watch each new mod, one to read the files of each mod with
    /// files to download, and a download's for each file.
    /// </summary>
    private IEnumerable<(SourceId Source, int Requests)> Needs(ListImportOptions options)
    {
        foreach (PlannedMod mod in _plan.Mods.Where(mod => mod.State != PlannedModState.NoSite))
        {
            SourceId source = mod.Listed.Mod.Game.Source;
            if (mod.State == PlannedModState.New && (mod.Listed.Watched || !options.Download))
            {
                yield return (source, 1);
            }

            int files = options.Download ? mod.Files.Count(file => !file.InLibrary && (options.IncludeOptional || !file.Listed.Optional)) : 0;
            if (files > 0)
            {
                yield return (source, 1 + (files * QuotaChecks.PerDownload));
            }
        }
    }

    private void ShowFacts()
    {
        Facts.Clear();
        string mods = Text.Format(Strings.ListImportModsValue, _plan.Mods.Count, _plan.NewMods, _plan.ModsInLibrary);
        if (_plan.ModsWithoutSite > 0)
        {
            mods += Text.Format(Strings.ListImportModsNoSite, _plan.ModsWithoutSite);
        }

        Facts.Add(new ImportListFact(Strings.ListImportModsLabel, mods));
        int files = _plan.FilesToDownload(IncludeOptional);
        string downloads = !DownloadFiles
            ? Strings.ListImportFilesWatchOnly
            : Counts.Of(files, Strings.ListImportFilesNone, Strings.ListImportFilesOne, Strings.ListImportFiles);
        Facts.Add(new ImportListFact(Strings.ListImportFilesLabel, downloads));
        if (_plan.List.FilesUnavailable > 0)
        {
            Facts.Add(new ImportListFact(
                Strings.ListImportUnavailableLabel,
                Counts.Of(_plan.List.FilesUnavailable, Strings.ListImportUnavailableOne, Strings.ListImportUnavailable)));
        }
        ImportCommand.NotifyCanExecuteChanged();
    }

    private void ShowProgress(int done)
    {
        int total = Math.Max(1, _plan.Mods.Count);
        ProgressPercent = 100.0 * done / total;
        ProgressText = Text.Format(Strings.ListImportProgress, done, _plan.Mods.Count);
    }
}
