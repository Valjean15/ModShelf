using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Watchlist;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The library by game. It opens on the games, as tiles or rows, each with how many mods it has, the updates found for
/// them, the space their files take and when they were last checked; a box filters them by name, and a list orders
/// them. A game opens on its mods, as rows or cards, which a box filters by name or author, toggles narrow to those with
/// updates, watched, with archived versions or with files missing from disk, and a list orders; with previews, each
/// shows its picture, from its saved details. A mod opens on its page in place of its game's mods, read from its site,
/// or from its saved details when the site can't give it. The recent activity shows in place of the games what happened
/// to the library's files lately, each opening its mod's page. It follows the library's changes while the app runs.
/// </summary>
public sealed partial class LibraryViewModel : ScreenViewModel
{
    private readonly IModLibrary _library;
    private readonly IModLookup _lookup;
    private readonly ISourceRegistry _sources;
    private readonly IHiddenGames _hiddenGames;
    private readonly IGameSettingsOpener _gameSettings;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly LibraryServices _services;
    private readonly ModPageServices _pageServices;
    private readonly Dictionary<ModRef, LibraryModViewModel> _mods = [];
    private readonly Dictionary<ModRef, LibraryMod> _shown = [];
    private readonly Dictionary<GameRef, LibraryGameViewModel> _games = [];
    private readonly HashSet<ModRef> _picturesShown = [];
    private CancellationToken _stopping;
    private CancellationTokenSource? _opening;
    private CancellationTokenSource? _checking;
    private ModRef? _openedMod;

    // While the selection changes in bulk, or is being shown, the mods' changes don't show it again each time.
    private bool _showingSelection;

    public LibraryViewModel(
        IModLibrary library,
        ILibraryHistory history,
        IModLookup lookup,
        ILibraryRescan rescan,
        ILibraryDownloads libraryDownloads,
        ILinkDownloads links,
        IWatchlists watchlists,
        ILibraryStorage storage,
        ISettingsService settings,
        IBrowserLauncher browser,
        IFolderOpener folders,
        IClipboard clipboard,
        IDialogService dialogs,
        ISourceRegistry sources,
        IHiddenGames hiddenGames,
        IGameSettingsOpener gameSettings,
        ModPictures pictures,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<LibraryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(hiddenGames);
        ArgumentNullException.ThrowIfNull(gameSettings);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _lookup = lookup;
        _sources = sources;
        _hiddenGames = hiddenGames;
        _gameSettings = gameSettings;
        _pictures = pictures;
        _uiThread = uiThread;
        _logger = logger;
        _services = new LibraryServices(library, rescan, settings, browser, folders, dialogs, pictures, OpenAsync, ShowSelection, timeProvider, logger);
        _pageServices = new ModPageServices(
            library,
            libraryDownloads,
            rescan,
            storage,
            settings,
            new FolderDownloads(links, dialogs),
            new TrackedMods(watchlists),
            sources,
            browser,
            folders,
            clipboard,
            dialogs,
            pictures,
            timeProvider,
            logger);
        _timeProvider = timeProvider;
        GameOrders =
        [
            new(GameOrder.MostMods, Strings.GameOrderMostMods),
            new(GameOrder.Name, Strings.GameOrderName),
            new(GameOrder.MostSpace, Strings.GameOrderMostSpace),
            new(GameOrder.UpdatesFirst, Strings.GameOrderUpdatesFirst),
        ];
        SelectedGameOrder = GameOrders[0];
        ModOrders =
        [
            new(ModOrder.Name, Strings.ModOrderName),
            new(ModOrder.UpdatesFirst, Strings.ModOrderUpdatesFirst),
            new(ModOrder.UpdatedOnSite, Strings.ModOrderUpdatedOnSite),
            new(ModOrder.Size, Strings.ModOrderSize),
            new(ModOrder.AddedToLibrary, Strings.ModOrderAdded),
        ];
        SelectedModOrder = ModOrders[0];
        ModFilters =
        [
            new(ModFilterKind.HasUpdates, Strings.ModFilterUpdates, ArrangeMods),
            new(ModFilterKind.Watched, Strings.ModFilterWatched, ArrangeMods),
            new(ModFilterKind.Archived, Strings.ModFilterArchived, ArrangeMods),
            new(ModFilterKind.Missing, Strings.ModFilterMissing, ArrangeMods),
        ];
        Activity = new LibraryActivityViewModel(history, OpenAsync, GameNameOf, timeProvider, logger);
        Previews = new PreviewsViewModel(PreviewScreen.Library, pictures);
        Previews.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PreviewsViewModel.Level))
            {
                ShowPictures();
            }
        };
    }

    public override Screen Screen => Screen.Library;

    public override string Title => Strings.LibraryTitle;

    /// <summary>
    /// Whether the mods show as rows, with thumbnails or not, or as cards; and the games too.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// The games with mods in the library whose names have <see cref="GamesFilter"/>, in <see cref="SelectedGameOrder"/>;
    /// the hidden ones only with <see cref="ShowHiddenGames"/>.
    /// </summary>
    public ObservableCollection<LibraryGameViewModel> Games { get; } = [];

    /// <summary>
    /// Whether the games the settings hide show too.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowHiddenGames { get; set; }

    /// <summary>
    /// Whether any of the library's games is hidden, so <see cref="ShowHiddenGames"/> shows.
    /// </summary>
    [ObservableProperty]
    public partial bool HasHiddenGames { get; private set; }

    /// <summary>
    /// Such as "Show hidden games (2)".
    /// </summary>
    [ObservableProperty]
    public partial string ShowHiddenGamesText { get; private set; } = string.Empty;

    /// <summary>
    /// What the games' names must have to show; empty for every game.
    /// </summary>
    [ObservableProperty]
    public partial string GamesFilter { get; set; } = string.Empty;

    public IReadOnlyList<LibraryChoice<GameOrder>> GameOrders { get; }

    [ObservableProperty]
    public partial LibraryChoice<GameOrder> SelectedGameOrder { get; set; }

    /// <summary>
    /// Such as "4 games · 112 mods · 41.8 GB on disk · checked 2 hours ago".
    /// </summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the library has no mods at all.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    /// <summary>
    /// Whether the library has games, but the filter hides them all; <see cref="NothingMatchesText"/> says so.
    /// </summary>
    [ObservableProperty]
    public partial bool NothingMatches { get; private set; }

    [ObservableProperty]
    public partial string NothingMatchesText { get; private set; } = string.Empty;

    /// <summary>
    /// The game whose mods show, in place of the games; null when the games show.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameOpen))]
    [NotifyCanExecuteChangedFor(nameof(ShowGamesCommand))]
    public partial LibraryGameViewModel? OpenGame { get; private set; }

    public bool IsGameOpen => OpenGame is not null;

    /// <summary>
    /// The open game's mods that pass <see cref="ModsFilter"/> and the toggles that are on, in
    /// <see cref="SelectedModOrder"/>.
    /// </summary>
    public ObservableCollection<LibraryModViewModel> GameMods { get; } = [];

    /// <summary>
    /// What the open game's mods' names or authors must have to show; empty for every mod.
    /// </summary>
    [ObservableProperty]
    public partial string ModsFilter { get; set; } = string.Empty;

    /// <summary>
    /// The toggles that narrow the open game's mods, each with how many it would show.
    /// </summary>
    public IReadOnlyList<ModFilterViewModel> ModFilters { get; }

    public IReadOnlyList<LibraryChoice<ModOrder>> ModOrders { get; }

    [ObservableProperty]
    public partial LibraryChoice<ModOrder> SelectedModOrder { get; set; }

    /// <summary>
    /// Whether the open game has mods, but the filter and toggles hide them all.
    /// </summary>
    [ObservableProperty]
    public partial bool NoModMatches { get; private set; }

    /// <summary>
    /// Such as "3 selected": the open game's mods selected, which are always among those shown.
    /// </summary>
    [ObservableProperty]
    public partial string SelectionText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    /// <summary>
    /// Such as "Update selected (2)": how many of the selected mods' updates can be downloaded.
    /// </summary>
    [ObservableProperty]
    public partial string UpdateSelectedText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the selected mods have updates to download, while the game's updates aren't being downloaded.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateSelectedCommand))]
    public partial bool CanUpdateSelected { get; private set; }

    /// <summary>
    /// Whether every mod shown is selected, none, or some (null). Setting it selects or unselects every mod shown.
    /// </summary>
    [ObservableProperty]
    public partial bool? AreAllShownSelected { get; set; } = false;

    /// <summary>
    /// The mod opened, in place of its game's mods; null when none is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModOpen))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(ShowGamesCommand))]
    public partial ModPageViewModel? Mod { get; private set; }

    public bool IsModOpen => Mod is not null;

    /// <summary>
    /// The way back from the mod opened: its game's name, back to its mods; or the recent activity's, when it was opened
    /// from there.
    /// </summary>
    [ObservableProperty]
    public partial string BackText { get; private set; } = string.Empty;

    /// <summary>
    /// What happened to the library's files lately.
    /// </summary>
    public LibraryActivityViewModel Activity { get; }

    /// <summary>
    /// Whether the recent activity shows, in place of the games.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowGamesCommand))]
    public partial bool IsShowingActivity { get; private set; }

    /// <summary>
    /// Whether a mod is being read from its site, to open it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpening { get; private set; }

    /// <summary>
    /// Why the mod opened shows its saved details rather than its site's; null when it shows its site's.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string? PageProblem { get; private set; }

    /// <summary>
    /// The last opening of a mod, for tests to wait on.
    /// </summary>
    internal Task Opening { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The last check of the open game's mods' files and saved details, for tests to wait on.
    /// </summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the previews from the settings, lists the library, then follows its changes while the app runs, and asks the
    /// games' sites for their names.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        await ReadHiddenGamesAsync(cancellationToken);
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
                foreach (LibraryMod mod in mods)
                {
                    Show(mod);
                }

                Arrange();
            },
            Apply,
            cancellationToken);
    }

    partial void OnGamesFilterChanged(string value) => Arrange();

    partial void OnShowHiddenGamesChanged(bool value) => Arrange();

    partial void OnSelectedGameOrderChanged(LibraryChoice<GameOrder> value) => Arrange();

    partial void OnModsFilterChanged(string value) => ArrangeMods();

    partial void OnSelectedModOrderChanged(LibraryChoice<ModOrder> value) => ArrangeMods();

    private bool CanShowGames() => IsGameOpen || IsModOpen || IsShowingActivity;

    /// <summary>
    /// Opens the open game's own settings, on the Settings screen.
    /// </summary>
    [RelayCommand]
    private void OpenGameSettings()
    {
        if (OpenGame is { } game)
        {
            _gameSettings.Open(game.Game);
        }
    }

    /// <summary>
    /// Reads which games the settings hide; settings that can't be read hide none, and the Settings screen says why.
    /// </summary>
    private async Task ReadHiddenGamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _hiddenGames.Show(await _pageServices.Settings.GetAsync(cancellationToken));
        }
        catch (InvalidDataException)
        {
            // The Settings screen says what's wrong.
        }
    }

    /// <summary>
    /// Shows the library's recent activity in place of the games, read again.
    /// </summary>
    [RelayCommand]
    private Task ShowActivityAsync()
    {
        IsShowingActivity = true;
        return Activity.LoadAsync(_stopping);
    }

    /// <summary>
    /// Closes the mod and the game, or the recent activity, back to the games.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowGames))]
    private void ShowGames()
    {
        CloseMod();
        _checking?.Cancel();
        IsShowingActivity = false;
        OpenGame = null;
        ArrangeMods();
        ShowPictures();
    }

    /// <summary>
    /// Closes the mod, back to its game's mods, or to the recent activity it was opened from.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsModOpen))]
    private void Back()
    {
        ModRef? closed = _openedMod;
        CloseMod();
        if (!IsShowingActivity && closed is { } mod && OpenGame?.Game != mod.Game && Games.FirstOrDefault(game => game.Game == mod.Game) is { } its)
        {
            Open(its);
        }
    }

    private bool CanRetry() => PageProblem is not null && _openedMod is not null;

    /// <summary>
    /// Reads the mod opened from its site again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync(CancellationToken cancellationToken) => _openedMod is { } mod ? OpenAsync(mod, cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// Opens the mod on its page, in place of the list: read from its site, or from its saved details when the site
    /// can't give it, saying why. A mod opened later drops one still being read.
    /// </summary>
    private Task OpenAsync(ModRef mod, CancellationToken cancellationToken)
    {
        Opening = OpenPageAsync(mod);
        return Opening;
    }

    private async Task OpenPageAsync(ModRef mod)
    {
        using CancellationTokenSource opening = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _opening, opening);
        dropped?.Cancel();
        CancellationToken token = opening.Token;
        IsOpening = true;
        try
        {
            ModPage page;
            string? problem = null;
            try
            {
                page = await _lookup.LookUpAsync(mod, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (OfflineReason(mod, exception) is { } reason)
            {
                page = await SavedPageAsync(mod, token);
                problem = reason;
            }

            token.ThrowIfCancellationRequested();
            ModPageViewModel opened = new(page, shown => _shown.GetValueOrDefault(shown), _pageServices, Previews, OpenAsync, _stopping)
            {
                IsOffline = problem is not null,
            };
            _openedMod = mod;
            BackText = IsShowingActivity ? Strings.LibraryActivity
                : _games.TryGetValue(mod.Game, out LibraryGameViewModel? game) ? game.Name
                : page.Game.Name;
            Mod = opened;
            PageProblem = problem;
            await opened.LoadAsync(token);
            if (problem is null)
            {
                await opened.ShowTrackedAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another mod was opened, the list shown again, or the app is closing.
        }
        catch (Exception exception)
        {
            LogOpenFailed(_logger, exception, mod.Id, mod.Game.Key);
            await _services.Dialogs.ShowMessageAsync(Strings.ActionFailedTitle, Strings.SomethingWentWrong, CancellationToken.None);
        }
        finally
        {
            if (ReferenceEquals(_opening, opening))
            {
                IsOpening = false;
                _opening = null;
            }
        }
    }

    /// <summary>
    /// Why the mod's site couldn't give it, to say over its saved details; null for a failure that isn't the site's.
    /// </summary>
    private string? OfflineReason(ModRef mod, Exception exception)
        => exception is ArgumentException
            ? Text.Format(Strings.ModPageOffline, _sources.SiteName(mod.Game.Source), exception.Message)
            : SiteProblems.Describe(exception, _sources.SiteName(mod.Game.Source), Strings.ModPageOffline);

    /// <summary>
    /// The mod's page from what's saved: its saved details, or the library's, with no files or changelog.
    /// </summary>
    private async Task<ModPage> SavedPageAsync(ModRef mod, CancellationToken cancellationToken)
    {
        ModInfo? saved = await _pictures.Details.GetSavedAsync(mod, cancellationToken);
        LibraryMod? inLibrary = _shown.GetValueOrDefault(mod);
        ModInfo details = saved ?? new ModInfo(mod, inLibrary?.Name ?? mod.Id, author: inLibrary?.Author, pageUrl: inLibrary?.PageUrl);
        string gameName = _games.TryGetValue(mod.Game, out LibraryGameViewModel? game) ? game.Name : mod.Game.Key;
        return new ModPage(new GameInfo(mod.Game, gameName), details, [], [], null);
    }

    /// <summary>
    /// Rescans every mod's folders, then says what it found. The screen shows what it added as the library changes.
    /// </summary>
    [RelayCommand]
    private Task RescanAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            () => Rescans.RunAsync(_services.Rescan, _services.Library, _services.Dialogs, null, cancellationToken),
            _services.Dialogs,
            exception => LogRescanFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// Shows what a change did. Runs on the UI thread.
    /// </summary>
    private void Apply(LibraryChange change)
    {
        if (change.After is { } after)
        {
            Show(after);
        }
        else
        {
            _mods.Remove(change.Mod);
            _shown.Remove(change.Mod);
            _picturesShown.Remove(change.Mod);
        }

        Arrange();
        if (change.After is { } changed && OpenGame?.Game == changed.Mod.Game)
        {
            Checking = CheckAsync([changed], _checking?.Token ?? _stopping);
        }

        // Also for a change to a mod the open mod needs.
        Mod?.ShowLibrary();
    }

    /// <summary>
    /// Shows the mod as it is now, adding its row, and its game, when they're new. A game is kept once made, so its site
    /// is asked for its name once.
    /// </summary>
    private void Show(LibraryMod mod)
    {
        _shown[mod.Mod] = mod;
        if (_mods.TryGetValue(mod.Mod, out LibraryModViewModel? shown))
        {
            shown.Show(mod);
        }
        else
        {
            LibraryModViewModel added = new(mod, _services);
            _mods.Add(mod.Mod, added);
            if (Previews.ShowsPictures && OpenGame?.Game == mod.Mod.Game && _picturesShown.Add(mod.Mod))
            {
                added.Picture.Show(mod.Mod, _stopping);
            }
        }

        if (!_games.ContainsKey(mod.Mod.Game))
        {
            LibraryGameViewModel game = new(mod.Mod.Game, Open, UpdateGameAsync, _timeProvider, _pictures);
            _games.Add(mod.Mod.Game, game);
            if (Previews.ShowsPictures)
            {
                game.Cover.Show(_stopping);
            }

            _ = NameAsync(mod.Mod.Game);
        }
    }

    /// <summary>
    /// Downloads the game's mods' updates into the library, mod by mod in order of name, then says what it did.
    /// </summary>
    private async Task UpdateGameAsync(LibraryGameViewModel game, CancellationToken cancellationToken)
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        LibraryUpdate[] updates =
        [
            .. _mods.Values
                .Where(mod => mod.Mod.Game == game.Game)
                .OrderBy(mod => mod.Name, byName)
                .SelectMany(mod => mod.DownloadableUpdates),
        ];
        await QueueAsync(game, updates, cancellationToken);
    }

    /// <summary>
    /// Downloads the selected mods' updates into the library, mod by mod as they're shown, then says what it did on the
    /// game's heading, and unselects them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private async Task UpdateSelectedAsync(CancellationToken cancellationToken)
    {
        if (OpenGame is not { } game)
        {
            return;
        }

        LibraryModViewModel[] selected = [.. GameMods.Where(mod => mod.IsSelected)];
        await QueueAsync(game, [.. selected.SelectMany(mod => mod.DownloadableUpdates)], cancellationToken);
        Unselect(selected);
    }

    /// <summary>
    /// Queues the updates, while the game says Update all and Update selected are busy, then says what it did.
    /// </summary>
    private async Task QueueAsync(LibraryGameViewModel game, IReadOnlyList<LibraryUpdate> updates, CancellationToken cancellationToken)
    {
        game.ShowUpdating(null, false);
        ShowSelection();
        (string Note, bool IsProblem) done = (string.Empty, false);
        try
        {
            done = await LibraryBatch.QueueAsync(
                _pageServices.LibraryDownloads,
                [.. updates.Select(update => (update.PageUrl!, update.File.Mod))],
                ofUpdates: true,
                _logger,
                cancellationToken);
        }
        finally
        {
            game.ShowUpdating(done.Note, done.IsProblem);
            ShowSelection();
        }
    }

    [RelayCommand]
    private void ClearSelection() => Unselect([.. _mods.Values]);

    /// <summary>
    /// Unselects the mods, telling the screen once.
    /// </summary>
    private void Unselect(IEnumerable<LibraryModViewModel> mods)
    {
        _showingSelection = true;
        try
        {
            foreach (LibraryModViewModel mod in mods)
            {
                mod.IsSelected = false;
            }
        }
        finally
        {
            _showingSelection = false;
        }

        ShowSelection();
    }

    partial void OnAreAllShownSelectedChanged(bool? value)
    {
        if (_showingSelection || value is null)
        {
            return;
        }

        _showingSelection = true;
        try
        {
            foreach (LibraryModViewModel mod in GameMods)
            {
                mod.IsSelected = value.Value;
            }
        }
        finally
        {
            _showingSelection = false;
        }

        ShowSelection();
    }

    /// <summary>
    /// Counts the selected mods, among those shown, and their updates that can be downloaded. Runs on the UI thread.
    /// </summary>
    private void ShowSelection()
    {
        if (_showingSelection)
        {
            return;
        }

        LibraryModViewModel[] selected = [.. GameMods.Where(mod => mod.IsSelected)];
        int updates = selected.Sum(mod => mod.DownloadableUpdates.Count);
        SelectionText = Text.Format(Strings.SelectionCount, selected.Length);
        HasSelection = selected.Length > 0;
        UpdateSelectedText = Text.Format(Strings.LibraryUpdateSelected, updates);
        CanUpdateSelected = updates > 0 && OpenGame is { IsUpdating: false };
        _showingSelection = true;
        try
        {
            AreAllShownSelected = selected.Length == 0 ? false : selected.Length == GameMods.Count ? true : null;
        }
        finally
        {
            _showingSelection = false;
        }
    }

    /// <summary>
    /// Shows the game's mods in place of the games, and checks their files and saved details.
    /// </summary>
    private void Open(LibraryGameViewModel game)
    {
        CloseMod();
        _checking?.Cancel();
        _checking = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        OpenGame = game;
        ArrangeMods();
        ShowPictures();
        Checking = CheckAsync([.. _shown.Values.Where(mod => mod.Mod.Game == game.Game)], _checking.Token);
    }

    /// <summary>
    /// The game's name, as its site says it; its key until then, or for a game the library no longer has.
    /// </summary>
    private string GameNameOf(GameRef game) => _games.TryGetValue(game, out LibraryGameViewModel? shown) ? shown.Name : game.Key;

    private void CloseMod()
    {
        _opening?.Cancel();
        _openedMod = null;
        Mod = null;
        PageProblem = null;
    }

    /// <summary>
    /// Counts the mods' files missing from disk, once the library's folder is known, and reads when their sites last
    /// updated them from their saved details; then shows both. Started on the UI thread.
    /// </summary>
    private async Task CheckAsync(IReadOnlyList<LibraryMod> mods, CancellationToken cancellationToken)
    {
        try
        {
            string? root;
            try
            {
                root = await _pageServices.Settings.GetLibraryRootAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                // Settings that can't be read: what's on disk isn't known.
                root = null;
            }

            Dictionary<ModRef, int> missing = root is null
                ? []
                : await Task.Run(
                    () => mods.ToDictionary(
                        mod => mod.Mod,
                        mod => mod.Files.Count(file => !_pageServices.Storage.FileExists(LibraryPaths.Resolve(root, file.RelativePath)))),
                    cancellationToken);
            Dictionary<ModRef, DateTimeOffset?> updated = [];
            foreach (LibraryMod mod in mods)
            {
                updated[mod.Mod] = (await _pictures.Details.GetSavedAsync(mod.Mod, cancellationToken))?.UpdatedAt;
            }

            _uiThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                foreach (LibraryMod mod in mods)
                {
                    if (_mods.TryGetValue(mod.Mod, out LibraryModViewModel? row))
                    {
                        row.ShowMissing(missing.GetValueOrDefault(mod.Mod));
                        row.ShowSiteUpdate(updated.GetValueOrDefault(mod.Mod));
                    }
                }

                ArrangeMods();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Another game was opened, the games shown again, or the app is closing.
        }
        catch (Exception exception)
        {
            LogCheckFailed(_logger, exception, OpenGame?.Game.Key ?? string.Empty);
        }
    }

    /// <summary>
    /// Puts the games in order: those with mods whose names have the filter, as the order says; then the open game's
    /// mods. A game whose last mod left the library goes, and when it's open, the games show again.
    /// </summary>
    private void Arrange()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        Dictionary<GameRef, LibraryModViewModel[]> byGame = _mods.Values.GroupBy(mod => mod.Mod.Game).ToDictionary(game => game.Key, game => game.ToArray());
        KeyValuePair<GameRef, string>[] named = [.. byGame.Keys.Select(game => KeyValuePair.Create(game, _games[game].Name))];
        foreach ((GameRef game, LibraryModViewModel[] mods) in byGame)
        {
            LibraryGameViewModel heading = _games[game];
            heading.Show(mods);
            heading.AlsoOnText = SameNamedGames.AlsoOn(SameNamedGames.Of(game, heading.Name, named), _sources);
        }

        string filter = GamesFilter.Trim();
        int hidden = byGame.Keys.Count(game => _hiddenGames.IsHidden(game));
        HasHiddenGames = hidden > 0;
        ShowHiddenGamesText = Text.Format(Strings.LibraryShowHidden, hidden);
        IEnumerable<LibraryGameViewModel> shown = byGame.Keys
            .Where(game => ShowHiddenGames || !_hiddenGames.IsHidden(game))
            .Select(game => _games[game])
            .Where(game => Has(game.Name, filter));
        IOrderedEnumerable<LibraryGameViewModel> ordered = SelectedGameOrder.Value switch
        {
            GameOrder.MostMods => shown.OrderByDescending(game => game.ModCount),
            GameOrder.MostSpace => shown.OrderByDescending(game => game.Size),
            GameOrder.UpdatesFirst => shown.OrderByDescending(game => game.UpdateCount),
            _ => shown.OrderBy(game => 0),
        };
        Games.SyncWith([.. ordered.ThenBy(game => game.Name, byName)]);

        long size = _mods.Values.Sum(mod => mod.Size);
        string summary = Text.Format(
            Strings.LibrarySummary,
            Counts.Of(byGame.Count, Strings.LibraryOneGame, Strings.LibraryGames),
            Counts.Of(_mods.Count, Strings.LibraryOneMod, Strings.LibraryMods),
            Sizes.Format(size));
        SummaryText = _mods.Values.Max(mod => mod.CheckedAt) is { } checkedAt
            ? Text.Format(Strings.LibraryCheckedAgo, summary, Dates.Ago(checkedAt, _timeProvider))
            : summary;
        IsEmpty = _mods.Count == 0;
        NothingMatches = !IsEmpty && Games.Count == 0;
        NothingMatchesText = NothingMatches ? Text.Format(Strings.LibraryNothingMatchesGames, filter) : string.Empty;

        if (OpenGame is { } open && !byGame.ContainsKey(open.Game))
        {
            _checking?.Cancel();
            OpenGame = null;
        }

        ArrangeMods();
    }

    /// <summary>
    /// Puts the open game's mods in order: those whose names or authors have the filter and that every toggle that's on
    /// says, as the order says, then by name. Each toggle counts the game's mods it says. A selected mod no longer shown
    /// is unselected.
    /// </summary>
    private void ArrangeMods()
    {
        ShowMods();
        Unselect([.. _mods.Values.Where(mod => mod.IsSelected && !GameMods.Contains(mod))]);
    }

    private void ShowMods()
    {
        if (OpenGame is not { } game)
        {
            GameMods.Clear();
            NoModMatches = false;
            return;
        }

        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        LibraryModViewModel[] mods = [.. _mods.Values.Where(mod => mod.Mod.Game == game.Game)];
        foreach (ModFilterViewModel toggle in ModFilters)
        {
            toggle.Count = mods.Count(mod => Says(toggle.Kind, mod));
        }

        string filter = ModsFilter.Trim();
        IEnumerable<LibraryModViewModel> shown = mods
            .Where(mod => Has(mod.Name, filter) || (_shown.GetValueOrDefault(mod.Mod)?.Author is { } author && Has(author, filter)))
            .Where(mod => ModFilters.All(toggle => !toggle.IsOn || Says(toggle.Kind, mod)));
        IOrderedEnumerable<LibraryModViewModel> ordered = SelectedModOrder.Value switch
        {
            ModOrder.UpdatesFirst => shown.OrderByDescending(mod => mod.UpdateCount > 0),
            ModOrder.UpdatedOnSite => shown.OrderByDescending(mod => mod.SiteUpdatedAt ?? DateTimeOffset.MinValue),
            ModOrder.Size => shown.OrderByDescending(mod => mod.Size),
            ModOrder.AddedToLibrary => shown.OrderByDescending(mod => mod.AddedAt),
            _ => shown.OrderBy(mod => 0),
        };
        GameMods.SyncWith([.. ordered.ThenBy(mod => mod.Name, byName)]);
        NoModMatches = GameMods.Count == 0;
    }

    /// <summary>
    /// Whether the text has the filter, as the user's culture compares text, case aside; any text has an empty filter.
    /// </summary>
    private static bool Has(string text, string filter)
        => filter.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, filter, CompareOptions.IgnoreCase) >= 0;

    private static bool Says(ModFilterKind kind, LibraryModViewModel mod) => kind switch
    {
        ModFilterKind.HasUpdates => mod.UpdateCount > 0,
        ModFilterKind.Watched => mod.IsWatched,
        ModFilterKind.Archived => mod.ArchivedCount > 0,
        ModFilterKind.Missing => mod.MissingCount > 0,
        _ => false,
    };

    /// <summary>
    /// Shows the games' covers and the open game's mods' pictures while the screen shows pictures, as adult mods'
    /// pictures should look now; hides every picture otherwise. A picture already shown isn't read again.
    /// </summary>
    private void ShowPictures()
    {
        foreach (LibraryGameViewModel game in _games.Values)
        {
            if (Previews.ShowsPictures)
            {
                game.Cover.Show(_stopping);
            }
            else
            {
                game.Cover.Hide();
            }
        }

        if (!Previews.ShowsPictures)
        {
            foreach (ModRef mod in _picturesShown)
            {
                _mods.GetValueOrDefault(mod)?.Picture.Hide();
            }

            _picturesShown.Clear();
            return;
        }

        foreach (LibraryModViewModel mod in _mods.Values)
        {
            if (_picturesShown.Contains(mod.Mod))
            {
                mod.Picture.Refresh(_stopping);
            }
            else if (OpenGame?.Game == mod.Mod.Game)
            {
                _picturesShown.Add(mod.Mod);
                mod.Picture.Show(mod.Mod, _stopping);
            }
        }
    }

    /// <summary>
    /// Asks the game's site for its name and its picture, once. Until it answers, or when it can't, the game goes by its
    /// key, its initial on its cover.
    /// </summary>
    private async Task NameAsync(GameRef game)
    {
        if (await GameNames.ReadAsync(_sources, game, _logger, _stopping) is not { } info)
        {
            return;
        }

        _uiThread.Post(() =>
        {
            if (_games.TryGetValue(game, out LibraryGameViewModel? heading))
            {
                heading.Name = info.Name;
                heading.Cover.Place(info.CoverUrl, _stopping);
                Arrange();
            }
        });
    }
}
