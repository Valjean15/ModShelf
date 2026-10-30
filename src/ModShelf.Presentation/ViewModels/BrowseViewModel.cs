using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;

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
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels;

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
            // Watching first and listing after, so no change from here on is missed.
            IAsyncEnumerable<LibraryChange> changes = _library.ChangesAsync(cancellationToken);
            IReadOnlyList<LibraryMod> mods = await _library.ListAsync(cancellationToken);
            _uiThread.Post(() =>
            {
                foreach (LibraryMod mod in mods)
                {
                    _inLibrary[mod.Mod] = mod;
                }

                if (_navigation.Current == Screen.Browse)
                {
                    GamesWork = ReadingGamesAsync();
                }
            });

            await foreach (LibraryChange change in changes)
            {
                _uiThread.Post(() => ShowLibrary(change));
            }
        }
        finally
        {
            _navigation.Navigated -= OnNavigated;
        }
    }

    /// <summary>
    /// The text as a link to open, when it's a complete one, such as https://… or nxm://…; null for words to search.
    /// </summary>
    private static Uri? AsLink(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? link) ? link : null;
    }

    /// <param name="failed">What to say of a refusal other than the account's, such as "{0} couldn't search: {1}".</param>
    private static string? Describe(Exception exception, string site, string failed)
        => exception is ArgumentException refused ? LinkProblems.MessageForUser(refused) : SiteProblems.Describe(exception, site, failed);

    partial void OnGameChanged(GameChoice? value)
    {
        if (value is not null)
        {
            Listing = SearchListingAsync();
        }
    }

    partial void OnSelectedSortChanged(SortChoice value) => SearchAgain();

    partial void OnIncludeAdultContentChanged(bool value) => SearchAgain();

    /// <summary>
    /// Reads the games of every site that can search, keeping the ones read when another site fails. The game the library
    /// has the most mods of is chosen, unless the user chose one already.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReadGames))]
    private Task ReadGamesAsync()
    {
        GamesWork = ReadingGamesAsync();
        return GamesWork;
    }

    private bool CanReadGames() => !IsReadingGames && GamesFailed;

    /// <summary>
    /// Searches the chosen game's mods for the words in the box, from the first page; or opens the mod the box's link is
    /// to. A search started meanwhile is dropped.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync()
    {
        Listing = AsLink(SearchText) is { } link ? OpenListingAsync(token => _lookup.LookUpAsync(link, token), source: null) : SearchListingAsync();
        return Listing;
    }

    /// <summary>
    /// Lists the next page: of the search's results, or of the tracked mods.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync()
    {
        if (IsShowingTracked && _trackedSource is { } source)
        {
            Listing = ListingAsync(source, Strings.BrowseTrackedFailed, token => ShowTrackedPageAsync(source, token));
        }
        else if (_query is { } query && _searched is { } game)
        {
            SearchQuery next = new(query.Text, query.Sort, _nextOffset, query.Count, query.Author, query.IncludeAdultContent, query.Direction);
            Listing = ListingAsync(game.Source, Strings.BrowseSearchFailed, token => ShowSearchAsync(game, next, token));
        }

        return Listing;
    }

    private bool CanLoadMore() => HasMore && !IsBusy;

    /// <summary>
    /// Lists the mods of the chosen game the account tracks on its site, read afresh, the highest IDs first: on sites
    /// that number their mods, the newest.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowTracked))]
    private Task ShowTrackedAsync()
    {
        if (Game is not { } game)
        {
            return Task.CompletedTask;
        }

        SourceId source = game.Game.Source;
        Listing = ListingAsync(
            source,
            Strings.BrowseTrackedFailed,
            async token =>
            {
                IReadOnlyList<ModRef> tracked = await _services.Tracked.ReadAsync(source, token);
                token.ThrowIfCancellationRequested();
                _query = null;
                _searched = null;
                _trackedSource = source;
                _trackedToShow =
                [
                    .. tracked
                        .Where(mod => mod.Game == game.Game)
                        .OrderByDescending(mod => mod.Id.Length)
                        .ThenByDescending(mod => mod.Id, StringComparer.Ordinal),
                ];
                Mod = null;
                Results.Clear();
                IsShowingTracked = true;
                string site = _services.Sources.SiteName(source);
                ResultsText = _trackedToShow.Count switch
                {
                    0 => Text.Format(Strings.BrowseTrackedNone, game.Name, site),
                    1 => Text.Format(Strings.BrowseTrackedOne, game.Name, site),
                    int count => Text.Format(Strings.BrowseTracked, count, game.Name, site),
                };
                HasMore = false;
                await ShowTrackedPageAsync(source, token);
            });
        return Listing;
    }

    private bool CanShowTracked() => Game is { } game && _services.Tracked.CanTrack(game.Game.Source);

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

    private void OnNavigated(object? sender, Screen screen)
    {
        if (screen == Screen.Browse)
        {
            _uiThread.Post(() => GamesWork = ReadingGamesAsync());
        }
    }

    /// <summary>
    /// Reads the games, unless they were read already, or are being read.
    /// </summary>
    private async Task ReadingGamesAsync()
    {
        if (_gamesRead || IsReadingGames)
        {
            return;
        }

        IReadOnlyList<SourceId> sources = _lookup.SearchableSources;
        if (sources.Count == 0)
        {
            Tell(Strings.BrowseNoSites, needsAccount: false);
            return;
        }

        IsReadingGames = true;
        GamesFailed = false;
        Tell(null, needsAccount: false);
        List<GameChoice> games = [];
        try
        {
            foreach (SourceId source in sources)
            {
                string site = _services.Sources.SiteName(source);
                try
                {
                    IReadOnlyList<GameInfo> read = await _lookup.GetGamesAsync(source, _stopping);
                    games.AddRange(read.Select(game => new GameChoice(
                        game.Game,
                        sources.Count == 1 ? game.Name : Text.Format(Strings.BrowseGameOnSite, game.Name, site))));
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (Describe(exception, site, Strings.BrowseGamesFailed) is { } problem)
                {
                    GamesFailed = true;
                    Tell(problem, exception is AuthenticationFailedException, games: true);
                }
                catch (Exception exception)
                {
                    LogGamesFailed(_logger, exception, source.Value);
                    GamesFailed = true;
                    Tell(Strings.SomethingWentWrong, needsAccount: false, games: true);
                }
            }
        }
        finally
        {
            IsReadingGames = false;
        }

        _gamesRead = !GamesFailed;
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        Games.SyncWith([.. games.OrderBy(game => game.Name, byName)]);
        if (Game is null && Favourite(games) is { } favourite)
        {
            Game = favourite;
        }
    }

    /// <summary>
    /// The game the library has the most mods of, among <paramref name="games"/>; null for an empty library.
    /// </summary>
    private GameChoice? Favourite(List<GameChoice> games)
    {
        Dictionary<GameRef, int> counts = _inLibrary.Keys.GroupBy(mod => mod.Game).ToDictionary(game => game.Key, game => game.Count());
        return games
            .Where(game => counts.ContainsKey(game.Game))
            .OrderByDescending(game => counts[game.Game])
            .ThenBy(game => game.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .FirstOrDefault();
    }

    /// <summary>
    /// Searches the chosen game again with the order and content asked for, when the list holds a search.
    /// </summary>
    private void SearchAgain()
    {
        if (_query is not null && Game is not null)
        {
            Listing = SearchListingAsync();
        }
    }

    /// <summary>
    /// Searches the chosen game's mods for the box's words, from the first page. A link in the box is left out.
    /// </summary>
    private Task SearchListingAsync()
    {
        if (Game is not { } game)
        {
            Tell(Strings.BrowseChooseGame, needsAccount: false);
            return Task.CompletedTask;
        }

        string? words = AsLink(SearchText) is null ? SearchText : null;
        SearchQuery query = new(words, SelectedSort.Sort, 0, PageSize, Author, IncludeAdultContent, SelectedSort.Direction);
        return ListingAsync(game.Game.Source, Strings.BrowseSearchFailed, token => ShowSearchAsync(game.Game, query, token));
    }

    /// <summary>
    /// Lists a page of a search's results: the first in place of the list, or the next after those listed, leaving out
    /// any listed already, as when the site's results moved between pages. The first page closes the mod opened.
    /// </summary>
    private async Task ShowSearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        SearchPage page = await _lookup.SearchAsync(game, query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Offset == 0)
        {
            Results.Clear();
            IsShowingTracked = false;
            _trackedSource = null;
            Mod = null;
            _query = query;
            _searched = game;
        }

        HashSet<ModRef> listed = [.. Results.Select(row => row.Mod)];
        foreach (ModInfo mod in page.Mods.Where(mod => listed.Add(mod.Mod)))
        {
            Results.Add(Row(mod, problem: null));
        }

        _nextOffset = page.Offset + page.Mods.Count;
        HasMore = page.HasMore;
        ResultsText = Counts.Of(page.TotalCount, Strings.BrowseNoMatches, Strings.BrowseOneMatch, Strings.BrowseMatches);
    }

    /// <summary>
    /// Lists the next page of the tracked mods, with their details from their site, a few at a time. A mod whose details
    /// can't be read is listed by its ID, saying why.
    /// </summary>
    private async Task ShowTrackedPageAsync(SourceId source, CancellationToken cancellationToken)
    {
        ModRef[] page = [.. _trackedToShow.Skip(Results.Count).Take(PageSize)];
        (ModInfo Mod, string? Problem)[] read = new (ModInfo, string?)[page.Length];
        IModCatalog? catalog = _services.Sources.Find<IModCatalog>(source);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, page.Length),
            new ParallelOptions { MaxDegreeOfParallelism = ReadsAtOnce, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                ModRef mod = page[index];
                ModInfo byId = new(mod, Text.Format(Strings.BrowseModNumbered, mod.Id));
                try
                {
                    read[index] = catalog is null ? (byId, null) : (await catalog.GetModAsync(mod, token), null);
                }
                catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
                {
                    read[index] = (byId, Text.Format(Strings.BrowseModUnreadable, exception.Message));
                }
            });
        cancellationToken.ThrowIfCancellationRequested();

        foreach ((ModInfo mod, string? problem) in read)
        {
            Results.Add(Row(mod, problem));
        }

        HasMore = Results.Count < _trackedToShow.Count;
    }

    private BrowseModViewModel Row(ModInfo mod, string? problem)
        => new(mod, problem, _inLibrary.GetValueOrDefault(mod.Mod), _services.TimeProvider, OpenAsync);

    /// <summary>
    /// Opens a mod of the list, read afresh from its site.
    /// </summary>
    private Task OpenAsync(ModRef mod, CancellationToken cancellationToken)
    {
        Listing = OpenListingAsync(token => _lookup.LookUpAsync(mod, token), mod.Game.Source);
        return Listing;
    }

    /// <param name="source">The mod's site, when known; null for a link's.</param>
    private Task OpenListingAsync(Func<CancellationToken, Task<ModPage>> lookUp, SourceId? source)
        => ListingAsync(
            source,
            Strings.BrowseOpenFailed,
            Strings.BrowseNoSuchMod,
            async token =>
            {
                ModPage page = await lookUp(token);
                token.ThrowIfCancellationRequested();
                ModPageViewModel opened = new(page, _inLibrary.GetValueOrDefault(page.Mod.Mod), _services);
                Mod = opened;
                await opened.ShowTrackedAsync(token);
            });

    /// <summary>
    /// Runs one listing at a time: one started later drops this one. What the site refuses is told the user.
    /// </summary>
    /// <param name="source">The site asked, to name in what's told; null for a link's, which isn't known.</param>
    /// <param name="failed">What to say when the site refuses, such as "{0} couldn't search: {1}".</param>
    private Task ListingAsync(SourceId? source, string failed, Func<CancellationToken, Task> list)
        => ListingAsync(source, failed, notFound: null, list);

    /// <param name="notFound">What to say when the site has no such thing (404); null to say it as any refusal.</param>
    /// <inheritdoc cref="ListingAsync(SourceId?, string, Func{CancellationToken, Task})"/>
    private async Task ListingAsync(SourceId? source, string failed, string? notFound, Func<CancellationToken, Task> list)
    {
        using CancellationTokenSource listing = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _listing, listing);
        try
        {
            dropped?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended meanwhile.
        }

        IsBusy = true;
        Tell(null, needsAccount: false);
        try
        {
            await list(listing.Token);
        }
        catch (OperationCanceledException) when (listing.IsCancellationRequested)
        {
            // Another listing replaced this one, or the app is closing.
        }
        catch (Exception exception) when (Explain(exception, source, failed, notFound) is { } problem)
        {
            Tell(problem, exception is AuthenticationFailedException);
        }
        catch (Exception exception)
        {
            LogListingFailed(_logger, exception);
            Tell(Strings.SomethingWentWrong, needsAccount: false);
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _listing, null, listing) == listing)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// What to tell the user of a site's refusal: a link's as the Downloads screen tells it, the rest naming the site.
    /// </summary>
    private string? Explain(Exception exception, SourceId? source, string failed, string? notFound)
    {
        if (source is not { } site)
        {
            return exception is UnsupportedLinkException ? Strings.BrowseUnknownLink : LinkProblems.Describe(exception, library: false);
        }

        if (notFound is not null && exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone })
        {
            return notFound;
        }

        return Describe(exception, _services.Sources.SiteName(site), failed);
    }

    /// <param name="problem">What went wrong; null for nothing.</param>
    /// <param name="games">Whether it's that the games couldn't be read.</param>
    private void Tell(string? problem, bool needsAccount, bool games = false)
    {
        Problem = problem;
        NeedsAccount = needsAccount;
        IsGamesProblem = games;
    }

    /// <summary>
    /// Shows what a change did to what the library has of the mod. Runs on the UI thread.
    /// </summary>
    private void ShowLibrary(LibraryChange change)
    {
        if (change.After is { } after)
        {
            _inLibrary[change.Mod] = after;
        }
        else
        {
            _inLibrary.Remove(change.Mod);
        }

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
