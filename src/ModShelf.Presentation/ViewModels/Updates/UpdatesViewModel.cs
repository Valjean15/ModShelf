using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Updates;

/// <summary>
/// The updates found for the watched mods, by game, then by mod and newest first, with checking now and downloading
/// them all, or a game's; with previews, each shows its mod's thumbnail. Once the screen is shown, each update shows its
/// version's entry in its mod's changelog. It follows the library's changes, so what a background check finds shows at
/// once, and an update leaves once its file is in the library. The badge counts the updates.
/// </summary>
public sealed partial class UpdatesViewModel : ScreenViewModel, IDisposable
{
    // How many changelogs are read from their sites at once.
    private const int ChangelogsAtOnce = 4;

    private readonly IModLibrary _library;
    private readonly IUpdateChecker _checker;
    private readonly ISourceRegistry _sources;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdatesViewModel> _logger;
    private readonly ISettingsService _settings;
    private readonly IHiddenGames _hiddenGames;
    private readonly UpdatesServices _services;
    private readonly Dictionary<ModRef, LibraryMod> _mods = [];
    private readonly Dictionary<ModFileRef, UpdateViewModel> _shown = [];
    private readonly Dictionary<GameRef, UpdateGameViewModel> _games = [];
    private readonly SemaphoreSlim _changelogs = new(ChangelogsAtOnce);
    private CancellationToken _stopping;
    private bool _picturesShown;
    private bool _wasShown;
    private TimeSpan? _interval;

    public UpdatesViewModel(
        IModLibrary library,
        IUpdateChecker checker,
        ILibraryDownloads downloads,
        ISourceRegistry sources,
        IModLookup lookup,
        IDialogService dialogs,
        ISettingsService settings,
        IHiddenGames hiddenGames,
        ModPictures pictures,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<UpdatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hiddenGames);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _checker = checker;
        _sources = sources;
        _settings = settings;
        _hiddenGames = hiddenGames;
        _pictures = pictures;
        _uiThread = uiThread;
        _timeProvider = timeProvider;
        _logger = logger;
        _services = new UpdatesServices(downloads, sources, lookup, dialogs, timeProvider, logger);
        Previews = new PreviewsViewModel(PreviewScreen.Updates, pictures);
        Previews.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PreviewsViewModel.Level))
            {
                ShowPictures();
            }
        };
    }

    public override Screen Screen => Screen.Updates;

    public override string Title => Strings.UpdatesTitle;

    /// <summary>
    /// Whether the updates show their mods' thumbnails.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// The games whose mods have updates, by name, each with its updates.
    /// </summary>
    public ObservableCollection<UpdateGameViewModel> Games { get; } = [];

    /// <summary>
    /// Every update, by its game, then by its mod's name, then the newest first.
    /// </summary>
    public ObservableCollection<UpdateViewModel> Updates { get; } = [];

    [ObservableProperty]
    public partial bool HasUpdates { get; private set; }

    /// <summary>
    /// Such as "3 updates for 2 mods · checked 2 hours ago, next in 4 hours"; empty without watched mods.
    /// </summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Update all (3)": how many updates can be downloaded.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateAllText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether any update can be downloaded, while the screen isn't downloading others.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    public partial bool CanUpdateAll { get; private set; }

    /// <summary>
    /// Whether Update all, the screen's or a game's, is downloading updates.
    /// </summary>
    [ObservableProperty]
    public partial bool IsUpdating { get; private set; }

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
    /// The changelogs being read, for tests to wait on.
    /// </summary>
    internal Task ReadingChanges { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reading the settings' update interval again, for tests to wait on.
    /// </summary>
    internal Task ReadingInterval { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the previews from the settings, lists the library's updates, then follows its changes while the app runs.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        _interval = await ReadIntervalAsync(cancellationToken);
        _hiddenGames.Changed += (_, _) => _uiThread.Post(Arrange);
        await _pictures.LoadAsync();
        Previews.Refresh();
        ShowPictures();
        _pictures.Changed += (_, _) => _uiThread.Post(() =>
        {
            Previews.Refresh();
            ShowPictures();
        });
        await LibraryFeed.FollowAsync(
            _library,
            _uiThread,
            mods =>
            {
                _mods.Keep(mods);
                Arrange();
            },
            change =>
            {
                _mods.Apply(change);
                Arrange();
            },
            cancellationToken);
    }

    public void Dispose() => _changelogs.Dispose();

    /// <summary>
    /// Reads the updates' changelog entries the first time the screen is shown, and those of updates found later; and
    /// the update interval again, which the Settings screen may have changed.
    /// </summary>
    public override void OnShown()
    {
        _wasShown = true;
        ReadChanges();
        ReadingInterval = ShowIntervalAsync();
    }

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
        await UserCommands.RunInPlaceAsync(
            async () =>
            {
                UpdateCheck check = await _checker.CheckAsync(cancellationToken);
                string found = Counts.Of(check.Found.Count, Strings.CheckNoNewUpdates, Strings.CheckOneNewUpdate, Strings.CheckNewUpdates);
                CheckResult = Text.Format(
                    Strings.CheckSummary,
                    Counts.Of(check.Checked, Strings.CheckOneMod, Strings.CheckMods),
                    Counts.Of(check.Requests, Strings.CheckOneRequest, Strings.CheckRequests),
                    found);
                foreach (UpdateCheckFailure failure in check.Failures)
                {
                    CheckFailures.Add(Describe(failure));
                }
            },
            exception => exception is InvalidDataException ? exception.Message : null,
            problem =>
            {
                CheckResult = problem;
                IsCheckResultAProblem = true;
            },
            exception => LogCheckFailed(_logger, exception),
            cancellationToken);
        IsChecking = false;
    }

    /// <summary>
    /// Downloads every update shown into the library, game by game, as Download does for one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private Task UpdateAllAsync(CancellationToken cancellationToken) => DownloadAsync([.. Updates], cancellationToken);

    private Task UpdateGameAsync(UpdateGameViewModel game, CancellationToken cancellationToken) => DownloadAsync([.. game.Updates], cancellationToken);

    /// <summary>
    /// Downloads the updates into the library, one after the other; each says what became of it. Update all waits until
    /// they're queued.
    /// </summary>
    private async Task DownloadAsync(IReadOnlyList<UpdateViewModel> updates, CancellationToken cancellationToken)
    {
        IsUpdating = true;
        ShowUpdateAll();
        try
        {
            foreach (UpdateViewModel update in updates.Where(update => update.CanDownload))
            {
                await update.QueueAsync(cancellationToken);
            }
        }
        finally
        {
            IsUpdating = false;
            ShowUpdateAll();
        }
    }

    /// <summary>
    /// Reads the changelog entries of the updates not read yet, a few at a time, once the screen has been shown.
    /// </summary>
    private void ReadChanges()
    {
        if (!_wasShown)
        {
            return;
        }

        ReadingChanges = Task.WhenAll(Updates.Select(update => update.ReadChangesAsync(_changelogs, _stopping)));
    }

    private async Task ShowIntervalAsync()
    {
        TimeSpan? interval = await ReadIntervalAsync(_stopping);
        _uiThread.Post(() =>
        {
            _interval = interval;
            ShowSummary();
        });
    }

    /// <summary>
    /// The interval between background checks; null when the settings can't be read. Takes the hidden games from them
    /// too.
    /// </summary>
    private async Task<TimeSpan?> ReadIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppSettings settings = await _settings.GetAsync(cancellationToken);
            _hiddenGames.Show(settings);
            return settings.UpdateInterval ?? UpdateInterval.Default;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks the game's site for its name, once; until it answers, the game goes by its key.
    /// </summary>
    private async Task NameAsync(GameRef game)
    {
        if (await GameNames.ReadAsync(_sources, game, _logger, _stopping) is not { } name)
        {
            return;
        }

        _uiThread.Post(() =>
        {
            _games[game].Name = name;
            Arrange();
        });
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

        return Text.Format(Strings.CheckGameFailed, failure.Game.Key, _sources.SiteName(failure.Game.Source), failure.Reason);
    }

    /// <summary>
    /// Shows each update's thumbnail while the screen shows them, as adult mods' pictures should look now; hides them
    /// otherwise. A picture already shown isn't read again.
    /// </summary>
    private void ShowPictures()
    {
        bool shown = Previews.ShowsPictures;
        foreach (UpdateViewModel row in _shown.Values)
        {
            if (!shown)
            {
                row.Picture.Hide();
            }
            else if (_picturesShown)
            {
                row.Picture.Refresh(_stopping);
            }
            else
            {
                row.Picture.Show(row.File.Mod, _stopping);
            }
        }

        _picturesShown = shown;
    }

    /// <summary>
    /// Shows every update the library has but hidden games', by game, then by its mod's name, then the newest first,
    /// keeping the rows of those shown already; and says how many there are, and when the watched mods were checked.
    /// </summary>
    private void Arrange()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        List<UpdateGameViewModel> games = [];
        foreach (IGrouping<GameRef, LibraryMod> game in _mods.Values
            .Where(mod => mod.Updates.Count > 0 && !_hiddenGames.IsHidden(mod.Mod.Game.Key))
            .GroupBy(mod => mod.Mod.Game))
        {
            List<UpdateViewModel> rows = [];
            foreach (LibraryMod mod in game.OrderBy(mod => mod.Name, byName))
            {
                foreach (LibraryUpdate update in mod.Updates.OrderByDescending(update => update.UploadedAt))
                {
                    rows.Add(RowFor(update, mod));
                }
            }

            UpdateGameViewModel heading = GameFor(game.Key);
            heading.Show(rows, IsUpdating);
            games.Add(heading);
        }

        Games.SyncWith([.. games.OrderBy(game => game.Name, byName)]);
        UpdateViewModel[] all = [.. Games.SelectMany(game => game.Updates)];
        foreach (ModFileRef gone in _shown.Keys.Except(all.Select(row => row.File)).ToList())
        {
            _shown.Remove(gone);
        }

        Updates.SyncWith(all);
        Badge = all.Length;
        HasUpdates = all.Length > 0;
        ShowUpdateAll();

        HasWatched = _mods.Values.Any(mod => mod.IsWatched);
        NoUpdatesText = HasWatched ? Strings.UpdatesNone : Strings.UpdatesNoneWatched;
        ShowSummary();
        ReadChanges();
    }

    private UpdateViewModel RowFor(LibraryUpdate update, LibraryMod mod)
    {
        if (_shown.TryGetValue(update.File, out UpdateViewModel? shown))
        {
            shown.Show(update, mod);
            return shown;
        }

        shown = new UpdateViewModel(update, mod, _services, _pictures);
        _shown.Add(update.File, shown);
        if (_picturesShown)
        {
            shown.Picture.Show(update.File.Mod, _stopping);
        }

        return shown;
    }

    /// <summary>
    /// The game's heading, made the first time one of its mods has an update. A game is kept once made, so its site is
    /// asked for its name once.
    /// </summary>
    private UpdateGameViewModel GameFor(GameRef game)
    {
        if (!_games.TryGetValue(game, out UpdateGameViewModel? heading))
        {
            heading = new UpdateGameViewModel(game, UpdateGameAsync);
            _games.Add(game, heading);
            _ = NameAsync(game);
        }

        return heading;
    }

    private void ShowUpdateAll()
    {
        int downloadable = Updates.Count(update => update.CanDownload);
        UpdateAllText = Text.Format(Strings.UpdatesUpdateAll, downloadable);
        CanUpdateAll = downloadable > 0 && !IsUpdating;
        foreach (UpdateGameViewModel game in Games)
        {
            game.Show([.. game.Updates], IsUpdating);
        }
    }

    /// <summary>
    /// Such as "3 updates for 2 mods · checked 2 hours ago, next in 4 hours": every watched mod was checked by then, and
    /// the background check is next an interval after the oldest check; or that some weren't checked yet.
    /// </summary>
    private void ShowSummary()
    {
        LibraryMod[] watched = [.. _mods.Values.Where(mod => mod.IsWatched)];
        if (watched.Length == 0)
        {
            SummaryText = string.Empty;
            return;
        }

        string count = Updates.Count == 0
            ? Strings.UpdatesNoUpdates
            : Text.Format(
                Strings.UpdatesForMods,
                Counts.Of(Updates.Count, Strings.LibraryOneUpdate, Strings.LibraryUpdates),
                Counts.Of(Updates.Select(update => update.File.Mod).Distinct().Count(), Strings.LibraryOneMod, Strings.LibraryMods));
        string checkedText;
        if (watched.Any(mod => mod.CheckedAt is null))
        {
            checkedText = Strings.UpdatesSomeNotChecked;
        }
        else
        {
            DateTimeOffset oldest = watched.Min(mod => mod.CheckedAt!.Value);
            string ago = Dates.Ago(oldest, _timeProvider);
            checkedText = _interval is { } interval
                ? Text.Format(Strings.UpdatesCheckedAgoNext, ago, Dates.In(oldest + interval - _timeProvider.GetUtcNow()))
                : Text.Format(Strings.UpdatesCheckedAgo, ago);
        }

        SummaryText = Text.Format(Strings.UpdatesSummary, count, checkedText);
    }
}
