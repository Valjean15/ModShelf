using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Common;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Reading the games the sites have, and choosing the one shown first.
/// </summary>
public sealed partial class BrowseViewModel
{
    /// <summary>
    /// What the toolbar offers: for the chosen game's site.
    /// </summary>
    private BrowseOffer _offer = BrowseChoices.Every;

    /// <summary>
    /// The last reading of the chosen game's own settings, for tests to wait on.
    /// </summary>
    internal Task DefaultsWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Offers what the chosen game's site can search by, shows its rows, reads its categories, and starts the toolbar as
    /// the game's own settings say.
    /// </summary>
    partial void OnGameChanged(GameChoice? value)
    {
        if (value is not null)
        {
            Offer(value.Game);
            ShowRows(value.Game, closeMod: true);
            CategoriesWork = ReadCategoriesAsync(value.Game);
            DefaultsWork = StartAsTheGameSaysAsync(value.Game);
        }
    }

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
        _allGames = games;
        ShowGameChoices();
        if (Game is null && Favourite(games) is { } favourite)
        {
            Game = favourite;
        }
    }

    /// <summary>
    /// Lists the sites' games by name, but those the settings hide.
    /// </summary>
    private void ShowGameChoices()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        Games.SyncWith([.. _allGames.Where(game => !_hiddenGames.IsHidden(game.Game)).OrderBy(game => game.Name, byName)]);
        ShowGameMatches();
    }

    partial void OnGameTextChanged(string value) => ShowGameMatches();

    /// <summary>
    /// Lists the games whose names match what's typed in the game box, those that start with it first.
    /// </summary>
    private void ShowGameMatches() => GameMatches.SyncWith(NameQuery.Parse(GameText).Find(Games, game => game.Name));

    /// <summary>
    /// Starts the toolbar as the game's own settings say, when it has some: its order, how recently its mods were
    /// updated, and its page size, Browse's own for any it leaves out. Left as it is for a game without, once a search
    /// replaced the rows, or once another game was chosen.
    /// </summary>
    private async Task StartAsTheGameSaysAsync(GameRef game)
    {
        AppSettings settings;
        try
        {
            settings = await _services.Settings.GetAsync(_stopping);
        }
        catch (InvalidDataException)
        {
            // The Settings screen says what's wrong.
            return;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }

        _hiddenGames.Show(settings);
        if (Game?.Game != game || !IsShowingRows || settings.GameFor(game).Browse is not { } defaults)
        {
            return;
        }

        _choosingQuietly = true;
        try
        {
            // An order or page size the game's site lacks, as one set by hand, starts on its own.
            SelectedSort = defaults.Sort is { } sort ? _offer.SortOf(sort, defaults.Direction) ?? _offer.Default : _offer.Default;
            SelectedTimeWindow = _offer.SameTimeWindow(defaults.UpdatedWithin);
            SelectedPageSize = _offer.SamePageSize(defaults.PageSize);
        }
        finally
        {
            _choosingQuietly = false;
        }
    }

    /// <summary>
    /// Offers the orders, filters and page sizes the game's site's search has, keeping each choice it has too, and its
    /// list of followed mods in its words.
    /// </summary>
    private void Offer(GameRef game)
    {
        SiteWords words = _services.Sources.Words(game.Source);
        BrowseOffer offer = BrowseChoices.For(_services.Sources.Find<IModSearch>(game.Source)?.Capabilities, words);
        _choosingQuietly = true;
        try
        {
            SortChoice sort = offer.SameSort(SelectedSort);
            TimeWindowChoice window = offer.SameTimeWindow(SelectedTimeWindow?.Within);
            PageSizeChoice size = offer.SamePageSize(SelectedPageSize?.Count);
            _offer = offer;
            Sorts = offer.Sorts;
            TimeWindows = offer.TimeWindows;
            PageSizes = offer.PageSizes;
            SelectedSort = sort;
            SelectedTimeWindow = window;
            SelectedPageSize = size;
            CanSearchByUpdated = offer.Has(SearchFilters.UpdatedSince);
            CanSearchByAuthor = offer.Has(SearchFilters.Author);
            CanSearchByCategory = offer.Has(SearchFilters.Category);
            CanIncludeAdult = offer.Has(SearchFilters.AdultContent);
        }
        finally
        {
            _choosingQuietly = false;
        }

        string? followed = _services.Tracked.CanTrack(game.Source) ? words.Followed : null;
        CanFollow = followed is not null;
        ShowFollowedText = followed is null ? string.Empty : Text.Format(Strings.BrowseShowFollowed, Text.Capitalized(followed));
    }

    /// <summary>
    /// Reads which games the settings hide; settings that can't be read hide none, and the Settings screen says why.
    /// </summary>
    private async Task ReadHiddenGamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _hiddenGames.Show(await _services.Settings.GetAsync(cancellationToken));
        }
        catch (InvalidDataException)
        {
            // The Settings screen says what's wrong.
        }
    }

    /// <summary>
    /// The game the library has the most mods of, among <paramref name="games"/>; null for an empty library.
    /// </summary>
    private GameChoice? Favourite(List<GameChoice> games)
    {
        Dictionary<GameRef, int> counts = _inLibrary.Keys.GroupBy(mod => mod.Game).ToDictionary(game => game.Key, game => game.Count());
        return games
            .Where(game => counts.ContainsKey(game.Game) && !_hiddenGames.IsHidden(game.Game))
            .OrderByDescending(game => counts[game.Game])
            .ThenBy(game => game.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .FirstOrDefault();
    }
}
