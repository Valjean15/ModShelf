using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The updates found for the watched mods, by mod and newest first, with checking now. It follows the library's changes,
/// so what a background check finds shows at once, and an update leaves once its file is in the library. The badge counts
/// the updates.
/// </summary>
public sealed partial class UpdatesViewModel : ScreenViewModel
{
    private readonly IModLibrary _library;
    private readonly IUpdateChecker _checker;
    private readonly ISourceRegistry _sources;
    private readonly IUiThread _uiThread;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdatesViewModel> _logger;
    private readonly UpdatesServices _services;
    private readonly Dictionary<ModRef, LibraryMod> _mods = [];
    private readonly Dictionary<ModFileRef, UpdateViewModel> _shown = [];

    public UpdatesViewModel(
        IModLibrary library,
        IUpdateChecker checker,
        ILibraryDownloads downloads,
        ISourceRegistry sources,
        IModLookup lookup,
        IDialogService dialogs,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<UpdatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _checker = checker;
        _sources = sources;
        _uiThread = uiThread;
        _timeProvider = timeProvider;
        _logger = logger;
        _services = new UpdatesServices(downloads, sources, lookup, dialogs, timeProvider, logger);
    }

    public override Screen Screen => Screen.Updates;

    public override string Title => Strings.UpdatesTitle;

    /// <summary>
    /// Every update, by its mod's name, then the newest first.
    /// </summary>
    public ObservableCollection<UpdateViewModel> Updates { get; } = [];

    [ObservableProperty]
    public partial bool HasUpdates { get; private set; }

    /// <summary>
    /// Whether any mod is watched, which checking needs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool HasWatched { get; private set; }

    /// <summary>
    /// What the screen says without updates: that none were found, or that no mod is watched.
    /// </summary>
    [ObservableProperty]
    public partial string NoUpdatesText { get; private set; } = Strings.UpdatesNoneWatched;

    /// <summary>
    /// When every watched mod was checked by, or that some weren't yet; empty without watched mods.
    /// </summary>
    [ObservableProperty]
    public partial string CheckedText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool IsChecking { get; private set; }

    /// <summary>
    /// What the last check from this screen did, such as "Checked 12 mods with 3 requests. 2 new updates."; or why it
    /// couldn't check. Null before any.
    /// </summary>
    [ObservableProperty]
    public partial string? CheckResult { get; private set; }

    [ObservableProperty]
    public partial bool IsCheckResultAProblem { get; private set; }

    /// <summary>
    /// The games and mods the last check couldn't check, and why.
    /// </summary>
    public ObservableCollection<string> CheckFailures { get; } = [];

    /// <summary>
    /// Lists the library's updates, then follows its changes while the app runs.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Watching first and listing after, so no change from here on is missed.
        IAsyncEnumerable<LibraryChange> changes = _library.ChangesAsync(cancellationToken);
        IReadOnlyList<LibraryMod> mods = await _library.ListAsync(cancellationToken);
        _uiThread.Post(() =>
        {
            foreach (LibraryMod mod in mods)
            {
                _mods[mod.Mod] = mod;
            }

            Arrange();
        });

        await foreach (LibraryChange change in changes)
        {
            _uiThread.Post(() => Apply(change));
        }
    }

    private static string Count(int count, string one, string many) => count == 1 ? one : Text.Format(many, count);

    private bool CanCheckNow() => HasWatched && !IsChecking;

    /// <summary>
    /// Checks the watched mods now, and says what the check did. It waits for a background check that's running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;
        CheckResult = Strings.UpdatesChecking;
        IsCheckResultAProblem = false;
        CheckFailures.Clear();
        try
        {
            UpdateCheck check = await _checker.CheckAsync(cancellationToken);
            string found = check.Found.Count switch
            {
                0 => Strings.CheckNoNewUpdates,
                1 => Strings.CheckOneNewUpdate,
                int count => Text.Format(Strings.CheckNewUpdates, count),
            };
            CheckResult = $"{Text.Format(Strings.CheckSummary, Count(check.Checked, Strings.CheckOneMod, Strings.CheckMods), Count(check.Requests, Strings.CheckOneRequest, Strings.CheckRequests))} {found}";
            foreach (UpdateCheckFailure failure in check.Failures)
            {
                CheckFailures.Add(Describe(failure));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (InvalidDataException exception)
        {
            CheckResult = exception.Message;
            IsCheckResultAProblem = true;
        }
        catch (Exception exception)
        {
            LogCheckFailed(_logger, exception);
            CheckResult = Strings.SomethingWentWrong;
            IsCheckResultAProblem = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Such as "Couldn't check SkyUI: …", or for a whole game, "Couldn't check the mods of fallout4 on Nexus Mods: …".
    /// </summary>
    private string Describe(UpdateCheckFailure failure)
    {
        if (failure.Mod is { } mod)
        {
            return Text.Format(Strings.CheckModFailed, _mods.GetValueOrDefault(mod)?.Name ?? mod.Id, failure.Reason);
        }

        string site = _sources.Find<IModSource>(failure.Game.Source)?.DisplayName ?? failure.Game.Source.Value;
        return Text.Format(Strings.CheckGameFailed, failure.Game.Key, site, failure.Reason);
    }

    /// <summary>
    /// Shows what a change did. Runs on the UI thread.
    /// </summary>
    private void Apply(LibraryChange change)
    {
        if (change.After is { } after)
        {
            _mods[change.Mod] = after;
        }
        else
        {
            _mods.Remove(change.Mod);
        }

        Arrange();
    }

    /// <summary>
    /// Shows every update the library has, by its mod's name, then the newest first, keeping the rows of those shown
    /// already; and says when the watched mods were checked.
    /// </summary>
    private void Arrange()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        List<UpdateViewModel> rows = [];
        foreach (LibraryMod mod in _mods.Values.Where(mod => mod.Updates.Count > 0).OrderBy(mod => mod.Name, byName))
        {
            foreach (LibraryUpdate update in mod.Updates.OrderByDescending(update => update.UploadedAt))
            {
                if (_shown.TryGetValue(update.File, out UpdateViewModel? shown))
                {
                    shown.Show(update, mod);
                }
                else
                {
                    shown = new UpdateViewModel(update, mod, _services);
                    _shown.Add(update.File, shown);
                }

                rows.Add(shown);
            }
        }

        foreach (ModFileRef gone in _shown.Keys.Except(rows.Select(row => row.File)).ToList())
        {
            _shown.Remove(gone);
        }

        Updates.SyncWith(rows);
        Badge = rows.Count;
        HasUpdates = rows.Count > 0;

        LibraryMod[] watched = [.. _mods.Values.Where(mod => mod.IsWatched)];
        HasWatched = watched.Length > 0;
        NoUpdatesText = HasWatched ? Strings.UpdatesNone : Strings.UpdatesNoneWatched;
        CheckedText = watched.Length == 0
            ? string.Empty
            : watched.Any(mod => mod.CheckedAt is null)
                ? Strings.UpdatesNotAllChecked
                : Text.Format(
                    Strings.UpdatesCheckedSince,
                    TimeZoneInfo.ConvertTime(watched.Min(mod => mod.CheckedAt!.Value), _timeProvider.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture));
    }
}
