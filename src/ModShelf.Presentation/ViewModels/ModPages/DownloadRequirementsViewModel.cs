using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Requirements;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// Download with requirements (ADR 0102): what a mod needs, as its site lists it, each mod the library lacks ticked to
/// download by its newest main file, unless the user unticks it; with "their requirements too", down to three levels.
/// Download queues the ticked ones, and the mod's own file when it came from one, asking first when the site's requests
/// look too few. The site's list isn't always right, so nothing is downloaded until the user says.
/// </summary>
public sealed partial class DownloadRequirementsViewModel : ObservableObject
{
    private readonly ModRef _mod;
    private readonly ModFile? _file;
    private readonly ModPageServices _services;
    private readonly HashSet<ModRef> _unticked = [];
    private readonly string _site;

    /// <param name="modName">The mod's name, for the title.</param>
    /// <param name="file">The mod's file downloaded with them; null for its requirements alone.</param>
    internal DownloadRequirementsViewModel(ModRef mod, string modName, ModFile? file, ModPageServices services)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(services);

        _mod = mod;
        _file = file;
        _services = services;
        _site = services.Sources.SiteName(mod.Game.Source);
        Title = Text.Format(file is null ? Strings.RequirementsTitleMissing : Strings.RequirementsTitle, modName);
        FileText = file is null ? null : Text.Format(Strings.RequirementsWithFile, file.Name);
    }

    /// <summary>
    /// Asks the dialog showing it to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public string Title { get; }

    /// <summary>
    /// The mod's own file downloaded with them, such as "With SkyUI 5.2"; null for the requirements alone.
    /// </summary>
    public string? FileText { get; }

    /// <summary>
    /// Each requirement, level by level.
    /// </summary>
    public ObservableCollection<RequirementRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Whether it's reading what the mod needs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeDepth))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsReading { get; private set; }

    /// <summary>
    /// Whether the ticked ones are being queued.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeDepth))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsQueueing { get; private set; }

    /// <summary>
    /// Whether it's queued them, and says what came of it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeDepth), nameof(CloseText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsDone { get; private set; }

    /// <summary>
    /// What the list says beside the rows, such as that the site lists nothing it needs; or why it couldn't be read.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial bool IsMessageAProblem { get; private set; }

    /// <summary>
    /// Whether the requirements of the mods to download are listed too, down to three levels.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludesDeeper { get; set; }

    /// <summary>
    /// Such as "Download (3)": the ticked requirements, and the mod's own file.
    /// </summary>
    [ObservableProperty]
    public partial string DownloadText { get; private set; } = string.Empty;

    /// <summary>
    /// What queueing did, once it's done.
    /// </summary>
    [ObservableProperty]
    public partial string? ResultText { get; private set; }

    [ObservableProperty]
    public partial bool IsResultAProblem { get; private set; }

    public bool CanChangeDepth => !IsReading && !IsQueueing && !IsDone;

    public string CloseText => IsDone ? Strings.DialogOk : Strings.DialogCancel;

    /// <summary>
    /// The files Download queues: the mod's own, and each ticked requirement's.
    /// </summary>
    internal int ToDownload => (_file is null ? 0 : 1) + Rows.Count(row => row.IsChosen);

    private bool CanDownload => !IsReading && !IsQueueing && !IsDone && ToDownload > 0;

    /// <summary>
    /// Reads what the mod needs, one level down. Runs on the UI thread.
    /// </summary>
    internal Task ReadAsync(CancellationToken cancellationToken) => PlanAsync(cancellationToken);

    partial void OnIncludesDeeperChanged(bool value) => _ = PlanAsync(CancellationToken.None);

    private async Task PlanAsync(CancellationToken cancellationToken)
    {
        IsReading = true;
        Message = Strings.RequirementsReading;
        IsMessageAProblem = false;
        try
        {
            RequirementsPlan plan = await _services.Requirements.PlanAsync(_mod, IncludesDeeper, cancellationToken);
            Show(plan);
        }
        catch (Exception exception) when (SiteProblems.Describe(exception, _site, Strings.RequirementsReadFailed) is { } problem)
        {
            ShowProblem(problem);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReadFailed(_services.Logger, exception, _mod.Id, _mod.Game.Key);
            ShowProblem(Strings.SomethingWentWrong);
        }
        finally
        {
            IsReading = false;
        }
    }

    private void ShowProblem(string problem)
    {
        Rows.Clear();
        Message = problem;
        IsMessageAProblem = true;
        ShowDownloadText();
    }

    private void Show(RequirementsPlan plan)
    {
        foreach (RequirementRowViewModel row in Rows)
        {
            row.ChosenChanged -= OnChosenChanged;
        }

        Rows.Clear();
        Dictionary<ModRef, string> names = plan.Requirements.Where(requirement => requirement.Mod is not null).ToDictionary(requirement => requirement.Mod!, requirement => requirement.Name);
        foreach (PlannedRequirement requirement in plan.Requirements)
        {
            RequirementRowViewModel row = new(
                requirement,
                names.GetValueOrDefault(requirement.NeededBy),
                chosen: requirement.Mod is not { } needed || !_unticked.Contains(needed),
                _services);
            row.ChosenChanged += OnChosenChanged;
            Rows.Add(row);
        }

        Message = !plan.Listed
            ? Text.Format(Strings.RequirementsNotListed, _site)
            : plan.Requirements.Count == 0
                ? Text.Format(Strings.RequirementsNone, _site)
                : plan.ToDownload.Count == 0 ? Strings.RequirementsNothingMissing : null;
        IsMessageAProblem = false;
        ShowDownloadText();
    }

    private void OnChosenChanged(object? sender, EventArgs e)
    {
        if (sender is RequirementRowViewModel { Requirement.Mod: { } mod } row)
        {
            _ = row.IsChosen ? _unticked.Remove(mod) : _unticked.Add(mod);
        }

        ShowDownloadText();
    }

    private void ShowDownloadText()
    {
        DownloadText = Text.Format(Strings.RequirementsDownload, ToDownload);
        DownloadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Queues the mod's own file, then each ticked requirement's, as Browse's Download selected does, and says what came
    /// of it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        List<(Uri Link, ModRef Mod)> items = [];
        if (_file?.PageUrl is { } own)
        {
            items.Add((own, _mod));
        }

        items.AddRange(Rows
            .Where(row => row is { IsChosen: true, Requirement: { File.PageUrl: not null, Mod: not null } })
            .Select(row => (row.Requirement.File!.PageUrl!, row.Requirement.Mod!)));
        if (!await QuotaChecks.GoOnAsync(
                _services.Sources,
                _services.Dialogs,
                _services.TimeProvider,
                items.Select(item => (item.Mod.Game.Source, QuotaChecks.PerDownload)),
                cancellationToken))
        {
            return;
        }

        IsQueueing = true;
        try
        {
            (string note, bool isProblem) = await LibraryBatch.QueueAsync(_services.LibraryDownloads, items, ofUpdates: false, _services.Logger, cancellationToken);
            ResultText = note;
            IsResultAProblem = isProblem;
            IsDone = true;
        }
        finally
        {
            IsQueueing = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
