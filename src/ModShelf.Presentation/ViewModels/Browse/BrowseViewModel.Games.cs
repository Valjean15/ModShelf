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
    /// The game quick search opened Browse on before the games were read; chosen once they are.
    /// </summary>
    private GameRef? _asked;

    /// <summary>
    /// The last reading of the chosen game's own settings, for tests to wait on.
    /// </summary>
    internal Task DefaultsWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Keeps the filters of the game chosen before, closing the filter card; then shows the chosen game's previews,
    /// offers what its site can search by, shows its rows and reads its categories. A game chosen before while the app
    /// runs gets its filters back; another starts the filters as its own settings say.
    /// </summary>
    partial void OnGameChanged(GameChoice? oldValue, GameChoice? newValue)
    {
        if (oldValue is not null)
        {
            KeepFilters(oldValue.Game);
        }

        Filters.Hide();
        Picker.ShowPicked(newValue?.Game);
        Previews.ShowFor(newValue?.Game);
        if (newValue is not null)
        {
            Offer(newValue.Game);
            BrowseFilterSet? kept = RestoreFilters(newValue.Game);
            ShowRows(newValue.Game, closeMod: true);
            CategoriesWork = ReadCategoriesAsync(newValue.Game, kept?.Category.Category);
            if (kept is null)
            {
                DefaultsWork = StartAsTheGameSaysAsync(newValue.Game);
            }
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
    /// Reads the games, unless they were read already, or are being read: each site's as soon as it answers, so a slow
    /// site keeps none of the others waiting; and of a site that finds its games by name, the library's.
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
        List<(GameChoice Choice, GameInfo Info)> games = [];
        try
        {
            List<Task<IReadOnlyList<GameInfo>?>> reading =
                [.. sources.Select(source => _byName.Finds(source) ? LibraryGamesAsync(source) : SiteGamesAsync(source))];
            while (reading.Count > 0)
            {
                Task<IReadOnlyList<GameInfo>?> done = await Task.WhenAny(reading);
                reading.Remove(done);
                if (await done is not { } read)
                {
                    continue;
                }

                if (_stopping.IsCancellationRequested)
                {
                    return;
                }

                foreach (GameInfo game in read)
                {
                    if (_byName.Finds(game.Game.Source))
                    {
                        Found(game);
                    }
                    else
                    {
                        games.Add((Choice(game), game));
                    }
                }

                _allGames = [.. games];
                ShowGameChoices();
            }
        }
        finally
        {
            IsReadingGames = false;
        }

        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        _gamesRead = !GamesFailed;
        _allGames = games;
        ShowGameChoices();
        GameRef? asked = _asked;
        _asked = null;
        if (asked is not null && Games.FirstOrDefault(choice => choice.Game == asked) is { } chosen)
        {
            Game = chosen;
        }
        else if (asked is not null && _byName.Finds(asked.Source))
        {
            await OpenFoundAsync(asked);
        }
        else if (Game is null && Favourite([.. games.Concat(_foundGames).Select(game => game.Choice)]) is { } favourite)
        {
            Game = favourite;
        }
    }

    /// <summary>
    /// The site's games, as it lists them; null when it couldn't, which is said, and when the app is closing.
    /// </summary>
    private async Task<IReadOnlyList<GameInfo>?> SiteGamesAsync(SourceId source)
    {
        string site = _services.Sources.SiteName(source);
        try
        {
            return await _lookup.GetGamesAsync(source, _stopping);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (Describe(exception, site, Strings.BrowseGamesFailed) is { } problem)
        {
            GamesFailed = true;
            Tell(problem, exception is AuthenticationFailedException, games: true);
            return null;
        }
        catch (Exception exception)
        {
            LogGamesFailed(_logger, exception, source.Value);
            GamesFailed = true;
            Tell(Strings.SomethingWentWrong, needsAccount: false, games: true);
            return null;
        }
    }

    /// <summary>
    /// The library's games of a site that finds its games by name, as it names them, or by their keys when it can't.
    /// </summary>
    private async Task<IReadOnlyList<GameInfo>?> LibraryGamesAsync(SourceId source)
    {
        GameRef[] games = [.. _inLibrary.Keys.Select(mod => mod.Game).Where(game => game.Source == source).Distinct()];
        return await Task.WhenAll(games.Select(async game => await GameNames.ReadAsync(_services.Sources, game, _logger, _stopping) ?? new GameInfo(game, game.Key)));
    }

    /// <summary>
    /// Offers the library's games of the sites that find theirs by name that aren't yet, as when a mod of a new one came
    /// into the library.
    /// </summary>
    private async Task OfferLibraryGamesByNameAsync()
    {
        HashSet<GameRef> offered = [.. _foundGames.Select(game => game.Info.Game)];
        foreach (SourceId source in _byName.Sites)
        {
            if (!_inLibrary.Keys.Any(mod => mod.Game.Source == source && !offered.Contains(mod.Game)))
            {
                continue;
            }

            foreach (GameInfo game in await LibraryGamesAsync(source) ?? [])
            {
                Found(game);
            }

            ShowGameChoices();
        }
    }

    /// <summary>
    /// Offers a game of a site that finds its games by name, the first time.
    /// </summary>
    private void Found(GameInfo game)
    {
        if (!_foundGames.Any(found => found.Info.Game == game.Game))
        {
            _foundGames.Add((Choice(game), game));
        }
    }

    /// <summary>
    /// How the game is named among the games to choose: with its site, when there are two sites or more.
    /// </summary>
    private GameChoice Choice(GameInfo game)
        => new(game.Game, _lookup.SearchableSources.Count == 1 ? game.Name : Text.Format(Strings.BrowseGameOnSite, game.Name, _services.Sources.SiteName(game.Game.Source)));

    /// <summary>
    /// Lists the sites' games by name, but those the settings hide, and offers them in the game picker.
    /// </summary>
    private void ShowGameChoices()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        (GameChoice Choice, GameInfo Info)[] shown =
        [
            .. _allGames.Concat(_foundGames.Where(found => !_allGames.Any(listed => listed.Info.Game == found.Info.Game)))
                .Where(game => !_hiddenGames.IsHidden(game.Choice.Game)),
        ];
        Games.SyncWith([.. shown.Select(game => game.Choice).OrderBy(game => game.Name, byName)]);
        Picker.ShowGames(shown.Select(game => game.Info));
    }

    /// <summary>
    /// Chooses the game picked in the game picker: one listed, or one found by name.
    /// </summary>
    private void ChooseGame(GameRef game)
    {
        if (Games.FirstOrDefault(choice => choice.Game == game) is { } listed)
        {
            Game = listed;
        }
        else if (Picker.Known(game) is { } found)
        {
            Found(found);
            ShowGameChoices();
            Game = Games.FirstOrDefault(choice => choice.Game == game) ?? Game;
        }
    }

    /// <summary>
    /// Chooses the game quick search opens Browse on; before the games are read, once they are. One of a site that finds
    /// its games by name is asked about first, when it isn't offered yet.
    /// </summary>
    private void OnGameOpening(object? sender, GameRef game)
    {
        if (Games.FirstOrDefault(choice => choice.Game == game) is { } chosen)
        {
            Game = chosen;
        }
        else if (_byName.Finds(game.Source) && !IsReadingGames)
        {
            GamesWork = OpenFoundAsync(game);
        }
        else
        {
            _asked = game;
        }
    }

    /// <summary>
    /// Offers a game of a site that finds its games by name, as the site names it, and chooses it.
    /// </summary>
    private async Task OpenFoundAsync(GameRef game)
    {
        GameInfo info = Picker.Known(game) ?? await GameNames.ReadAsync(_services.Sources, game, _logger, _stopping) ?? new GameInfo(game, game.Key);
        Found(info);
        ShowGameChoices();
        Game = Games.FirstOrDefault(choice => choice.Game == game) ?? Game;
    }

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
            SelectedSort = StartingSort(defaults);
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

        ShowChips();

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
