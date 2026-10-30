using System.Globalization;

using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Reading the games the sites have, and choosing the one shown first.
/// </summary>
public sealed partial class BrowseViewModel
{
    partial void OnGameChanged(GameChoice? value)
    {
        if (value is not null)
        {
            Listing = SearchListingAsync();
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
}
