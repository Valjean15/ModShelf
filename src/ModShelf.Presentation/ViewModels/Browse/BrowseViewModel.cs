using System.Collections.ObjectModel;

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
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A game's mods: rows of its trending, newest and recently updated mods until a search; searching them, by words,
/// author, category and how recently updated, in an order and a page at a time; or listing the ones the account tracks.
/// Opening one, from a list or from a link, shows its files and changelog, to download them, watch it and track it. The
/// games are read from the sites the first time the screen is shown. What the library has of each mod follows the
/// library's changes.
/// </summary>
public sealed partial class BrowseViewModel : ScreenViewModel
{
    /// <summary>
    /// How many mods a page lists, unless the user chooses otherwise.
    /// </summary>
    internal const int PageSize = 20;

    /// <summary>
    /// How many mods a row of a game's page lists at most.
    /// </summary>
    internal const int RowSize = 10;

    /// <summary>
    /// How many tracked mods' details are read from their site at once.
    /// </summary>
    private const int ReadsAtOnce = 4;

    private readonly IModLookup _lookup;
    private readonly IModLibrary _library;
    private readonly INavigationService _navigation;
    private readonly IHiddenGames _hiddenGames;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly ILogger<BrowseViewModel> _logger;
    private readonly ModPageServices _services;
    private readonly Dictionary<ModRef, LibraryMod> _inLibrary = [];
    private CancellationToken _stopping;
    private CancellationTokenSource? _listing;
    private SearchQuery? _query;
    private GameRef? _searched;
    private int _nextOffset;
    private IReadOnlyList<ModRef> _trackedToShow = [];
    private SourceId? _trackedSource;
    private bool _gamesRead;

    // Every game the sites listed, the hidden ones too.
    private List<GameChoice> _allGames = [];
    private bool _picturesShown;

    // While the screen changes a choice itself, such as the category when the game changes, that's no search.
    private bool _choosingQuietly;

    public BrowseViewModel(
        IModLookup lookup,
        IModLibrary library,
        ILibraryDownloads libraryDownloads,
        ILinkDownloads links,
        IWatchlists watchlists,
        ILibraryRescan rescan,
        ILibraryStorage storage,
        ISettingsService settings,
        ISourceRegistry sources,
        IBrowserLauncher browser,
        IFolderOpener folders,
        IClipboard clipboard,
        IDialogService dialogs,
        INavigationService navigation,
        IHiddenGames hiddenGames,
        ModPictures pictures,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<BrowseViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(hiddenGames);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _lookup = lookup;
        _library = library;
        _navigation = navigation;
        _hiddenGames = hiddenGames;
        _pictures = pictures;
        _uiThread = uiThread;
        _logger = logger;
        _services = new ModPageServices(
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
        SelectedSort = Sorts[0];
        SelectedTimeWindow = TimeWindows[0];
        SelectedPageSize = PageSizes[0];
        Categories = [_allCategories];
        SelectedCategory = _allCategories;
        Rows =
        [
            new BrowseRowViewModel(BrowseRowKind.Trending, Strings.BrowseTrending, showAll: null),
            new BrowseRowViewModel(BrowseRowKind.RecentlyAdded, Strings.BrowseRecentlyAdded, ShowAllAsync),
            new BrowseRowViewModel(BrowseRowKind.RecentlyUpdated, Strings.BrowseRecentlyUpdated, ShowAllAsync),
        ];
        Results.CollectionChanged += (_, _) => ShowSelection();
        Previews = new PreviewsViewModel(PreviewScreen.Browse, pictures);
        Previews.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PreviewsViewModel.Level))
            {
                ShowPictures();
            }
        };
    }

    public override Screen Screen => Screen.Browse;

    public override string Title => Strings.BrowseTitle;

    /// <summary>
    /// The games the sites that can search have, by name.
    /// </summary>
    public ObservableCollection<GameChoice> Games { get; } = [];

    /// <summary>
    /// The game whose mods are searched; null until one is chosen. Choosing one searches it.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowTrackedCommand))]
    public partial GameChoice? Game { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadGamesCommand))]
    public partial bool IsReadingGames { get; private set; }

    /// <summary>
    /// Whether the games couldn't all be read, so reading them can be tried again.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadGamesCommand))]
    public partial bool GamesFailed { get; private set; }

    /// <summary>
    /// Words to look for in the mods' names, or a link to a mod to open.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Only mods by this author; empty for any.
    /// </summary>
    [ObservableProperty]
    public partial string Author { get; set; } = string.Empty;

    /// <summary>
    /// The orders a search's results can come in.
    /// </summary>
    public IReadOnlyList<SortChoice> Sorts { get; } = BrowseChoices.Sorts;

    /// <summary>
    /// The order of the results. Changing it searches again, or searches in place of the rows.
    /// </summary>
    [ObservableProperty]
    public partial SortChoice SelectedSort { get; set; }

    /// <summary>
    /// How recently the mods a search finds were updated: any time, or in the last day, week or month.
    /// </summary>
    public IReadOnlyList<TimeWindowChoice> TimeWindows { get; } = BrowseChoices.TimeWindows;

    /// <summary>
    /// How recently the mods found were updated. Changing it searches again, or searches in place of the rows.
    /// </summary>
    [ObservableProperty]
    public partial TimeWindowChoice SelectedTimeWindow { get; set; }

    /// <summary>
    /// Every category, then the chosen game's, by name, once its site says them.
    /// </summary>
    public ObservableCollection<CategoryChoice> Categories { get; }

    /// <summary>
    /// The category of the mods found; null, or the first, for every category. Changing it searches again, or searches
    /// in place of the rows. Choosing another game chooses every category.
    /// </summary>
    [ObservableProperty]
    public partial CategoryChoice? SelectedCategory { get; set; }

    /// <summary>
    /// How many mods a page can list.
    /// </summary>
    public IReadOnlyList<PageSizeChoice> PageSizes { get; } = BrowseChoices.PageSizes;

    /// <summary>
    /// How many mods a page lists. Changing it searches again, or searches in place of the rows.
    /// </summary>
    [ObservableProperty]
    public partial PageSizeChoice SelectedPageSize { get; set; }

    /// <summary>
    /// Whether searches include mods with adult content. Changing it searches again.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeAdultContent { get; set; }

    /// <summary>
    /// Whether the mods show as rows, with thumbnails or not, or as cards.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// The mods the search found, or the account tracks, as far as listed.
    /// </summary>
    public ObservableCollection<BrowseModViewModel> Results { get; } = [];

    /// <summary>
    /// Such as "1,234 mods match."; empty before anything was listed.
    /// </summary>
    [ObservableProperty]
    public partial string ResultsText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the list holds the tracked mods, rather than a search's results.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShowingTracked { get; private set; }

    /// <summary>
    /// Whether more mods can be listed after these.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool HasMore { get; private set; }

    /// <summary>
    /// Whether the screen is waiting on a site: searching, listing or opening a mod.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// What went wrong the last time the screen asked a site, for the user to act on; null when nothing did.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Whether <see cref="Problem"/> is a site that needs an account linked, which the Accounts screen does.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsAccount { get; private set; }

    /// <summary>
    /// Whether <see cref="Problem"/> is that the games couldn't be read, so reading them again is offered with it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsGamesProblem { get; private set; }

    /// <summary>
    /// The mod opened, in place of the list; null when none is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModOpen))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial ModPageViewModel? Mod { get; private set; }

    public bool IsModOpen => Mod is not null;

    /// <summary>
    /// The last search, listing or opening, for tests to wait on.
    /// </summary>
    internal Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The last reading of the games, for tests to wait on.
    /// </summary>
    internal Task GamesWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the previews from the settings, lists the library, and follows its changes while the app runs, so each mod
    /// shows what the library has of it. The games are read the first time the screen is shown.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        await ReadHiddenGamesAsync(cancellationToken);
        _hiddenGames.Changed += (_, _) => _uiThread.Post(ShowGameChoices);
        await _pictures.LoadAsync();
        Previews.Refresh();
        ShowPictures();
        _pictures.Changed += (_, _) => _uiThread.Post(() =>
        {
            Previews.Refresh();
            ShowPictures();
        });
        _navigation.Navigated += OnNavigated;
        try
        {
            await LibraryFeed.FollowAsync(
                _library,
                _uiThread,
                mods =>
                {
                    _inLibrary.Keep(mods);
                    if (_navigation.Current == Screen.Browse)
                    {
                        GamesWork = ReadingGamesAsync();
                    }
                },
                ShowLibrary,
                cancellationToken);
        }
        finally
        {
            _navigation.Navigated -= OnNavigated;
        }
    }

    partial void OnSelectedSortChanged(SortChoice value) => SearchAgain();

    partial void OnSelectedTimeWindowChanged(TimeWindowChoice value) => SearchAgain();

    partial void OnSelectedCategoryChanged(CategoryChoice? value) => SearchAgain();

    partial void OnSelectedPageSizeChanged(PageSizeChoice value) => SearchAgain();

    /// <summary>
    /// Searches again with adult content or without it, or lists the rows again.
    /// </summary>
    partial void OnIncludeAdultContentChanged(bool value)
    {
        if (IsShowingRows && Game is { } game)
        {
            RowsWork = ListRowsAsync(game.Game);
        }
        else
        {
            SearchAgain();
        }
    }

    /// <summary>
    /// Closes the mod, back to the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsModOpen))]
    private void Back() => Mod = null;

    /// <summary>
    /// Shows the Accounts screen, to link the account a site asked for.
    /// </summary>
    [RelayCommand]
    private void GoToAccounts() => _navigation.NavigateTo(Screen.Accounts);

    /// <summary>
    /// Shows each mod's picture while the screen shows pictures, as adult mods' pictures should look now; hides them
    /// otherwise. A picture already shown isn't read again.
    /// </summary>
    private void ShowPictures()
    {
        bool shown = Previews.ShowsPictures;
        foreach (BrowseModViewModel row in Results.Concat(Rows.SelectMany(listed => listed.Mods)))
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
                row.Picture.Show(row.Details, _stopping);
            }
        }

        _picturesShown = shown;
    }

    /// <summary>
    /// Shows what a change did to what the library has of the mod. Runs on the UI thread.
    /// </summary>
    private void ShowLibrary(LibraryChange change)
    {
        _inLibrary.Apply(change);
        foreach (BrowseModViewModel row in Results.Concat(Rows.SelectMany(listed => listed.Mods)).Where(row => row.Mod == change.Mod))
        {
            row.ShowLibrary(change.After);
        }

        // Also for a change to a mod the open mod needs.
        Mod?.ShowLibrary();
    }
}
