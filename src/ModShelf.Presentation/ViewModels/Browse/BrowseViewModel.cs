using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Application.Watchlist;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Searching a game's mods, a page at a time, or listing the ones the account tracks; and opening one, from the list or
/// from a link, to see its files and changelog, download them, watch it and track it. The games are read from the sites
/// the first time the screen is shown. What the library has of each mod follows the library's changes.
/// </summary>
public sealed partial class BrowseViewModel : ScreenViewModel
{
    /// <summary>
    /// How many mods a page lists.
    /// </summary>
    internal const int PageSize = 20;

    /// <summary>
    /// How many tracked mods' details are read from their site at once.
    /// </summary>
    private const int ReadsAtOnce = 4;

    private readonly IModLookup _lookup;
    private readonly IModLibrary _library;
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;
    private readonly ILogger<BrowseViewModel> _logger;
    private readonly BrowseServices _services;
    private readonly Dictionary<ModRef, LibraryMod> _inLibrary = [];
    private CancellationToken _stopping;
    private CancellationTokenSource? _listing;
    private SearchQuery? _query;
    private GameRef? _searched;
    private int _nextOffset;
    private IReadOnlyList<ModRef> _trackedToShow = [];
    private SourceId? _trackedSource;
    private bool _gamesRead;

    public BrowseViewModel(
        IModLookup lookup,
        IModLibrary library,
        ILibraryDownloads libraryDownloads,
        ILinkDownloads links,
        IWatchlists watchlists,
        ISourceRegistry sources,
        IBrowserLauncher browser,
        IDialogService dialogs,
        INavigationService navigation,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<BrowseViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _lookup = lookup;
        _library = library;
        _navigation = navigation;
        _uiThread = uiThread;
        _logger = logger;
        _services = new BrowseServices(
            library,
            libraryDownloads,
            new FolderDownloads(links, dialogs),
            new TrackedMods(watchlists),
            sources,
            browser,
            dialogs,
            timeProvider,
            logger);
        SelectedSort = Sorts[0];
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
    public IReadOnlyList<SortChoice> Sorts { get; } =
    [
        new(SearchSort.Relevance, SearchDirection.Descending, Strings.BrowseSortBestMatch),
        new(SearchSort.Endorsements, SearchDirection.Descending, Strings.BrowseSortMostEndorsed),
        new(SearchSort.Endorsements, SearchDirection.Ascending, Strings.BrowseSortLeastEndorsed),
        new(SearchSort.Downloads, SearchDirection.Descending, Strings.BrowseSortMostDownloaded),
        new(SearchSort.Downloads, SearchDirection.Ascending, Strings.BrowseSortLeastDownloaded),
        new(SearchSort.Updated, SearchDirection.Descending, Strings.BrowseSortRecentlyUpdated),
        new(SearchSort.Updated, SearchDirection.Ascending, Strings.BrowseSortLongestSinceUpdated),
        new(SearchSort.Newest, SearchDirection.Descending, Strings.BrowseSortNewest),
        new(SearchSort.Newest, SearchDirection.Ascending, Strings.BrowseSortOldest),
    ];

    /// <summary>
    /// The order of the results. Changing it searches again.
    /// </summary>
    [ObservableProperty]
    public partial SortChoice SelectedSort { get; set; }

    /// <summary>
    /// Whether searches include mods with adult content. Changing it searches again.
    /// </summary>
    [ObservableProperty]
    public partial bool IncludeAdultContent { get; set; }

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
    /// Lists the library, and follows its changes while the app runs, so each mod shows what the library has of it. The
    /// games are read the first time the screen is shown.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
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

    partial void OnIncludeAdultContentChanged(bool value) => SearchAgain();

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
    /// Shows what a change did to what the library has of the mod. Runs on the UI thread.
    /// </summary>
    private void ShowLibrary(LibraryChange change)
    {
        _inLibrary.Apply(change);
        foreach (BrowseModViewModel row in Results.Where(row => row.Mod == change.Mod))
        {
            row.ShowLibrary(change.After);
        }

        if (Mod?.Mod == change.Mod)
        {
            Mod.ShowLibrary(change.After);
        }
    }
}
