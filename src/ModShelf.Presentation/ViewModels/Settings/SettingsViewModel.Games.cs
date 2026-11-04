using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The Games category: the library's games, those with settings of their own and those added, by name, and the chosen
/// one's own settings.
/// </summary>
public sealed partial class SettingsViewModel
{
    // The library's games; their names and pictures once read; and the games added. A game is its site and its key there,
    // in any case, as the settings have it.
    private readonly HashSet<GameRef> _libraryGames = new(AppSettings.GameComparer);
    private readonly Dictionary<GameRef, GameInfo> _gameInfos = new(AppSettings.GameComparer);
    private readonly HashSet<GameRef> _addedGames = new(AppSettings.GameComparer);

    /// <summary>
    /// The games, by name: the library's, those with settings of their own, and those added.
    /// </summary>
    public ObservableCollection<GameRowViewModel> Games { get; } = [];

    /// <summary>
    /// Whether there's no game to list.
    /// </summary>
    [ObservableProperty]
    public partial bool HasNoGames { get; private set; } = true;

    /// <summary>
    /// The game whose settings show; the first when none was chosen.
    /// </summary>
    [ObservableProperty]
    public partial GameRowViewModel? SelectedGame { get; set; }

    /// <summary>
    /// The chosen game's own settings; null while there's no game.
    /// </summary>
    [ObservableProperty]
    public partial GameSettingsViewModel? Game { get; private set; }

    /// <summary>
    /// The last reading of the library's games and their names, for tests to wait on.
    /// </summary>
    internal Task GamesWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the library's games again, which may have changed while another screen showed.
    /// </summary>
    public override void OnShown() => GamesWork = ReadGamesAsync();

    /// <summary>
    /// Shows the chosen game's own settings.
    /// </summary>
    partial void OnSelectedGameChanged(GameRowViewModel? value)
    {
        if (value is null)
        {
            Game = null;
            return;
        }

        if (Game is not null && AppSettings.GameComparer.Equals(Game.Game, value.Game))
        {
            return;
        }

        GameRef game = value.Game;
        GameSettingsViewModel page = new(
            game,
            value.Name,
            change => ChangeAsync(token => _settings.SetGameAsync(game, change, token), sayProblem: false),
            SaveFoldersAsync,
            other => CopyGameAsync(other, game),
            BrowseChoices.For(_sources.Find<IModSearch>(game.Source)?.Capabilities, _sources.Words(game.Source)),
            LibraryRoot);
        page.Show(_shown, LibraryRoot);
        ShowSameNamed(page);
        Game = page;
    }

    /// <summary>
    /// What a game's row says of its settings: none of its own, how many, or that it's hidden.
    /// </summary>
    private static string NoteOf(GameSettings own)
    {
        int overridden = new[]
        {
            own.Folders is not null,
            own.OldVersions is not null,
            own.CheckUpdates is not null || own.UpdateInterval is not null,
            own.Notify is not null,
            own.Browse is not null,
        }.Count(set => set);
        string count = overridden == 1 ? Strings.GameOneOverride : Text.Format(Strings.GameOverrides, overridden);
        return (own.Hidden == true, overridden) switch
        {
            (true, 0) => Strings.GameHiddenNote,
            (true, _) => Text.Format(Strings.GameHiddenAndOverrides, count),
            (false, 0) => Strings.GameUsesDefaults,
            _ => count,
        };
    }

    /// <summary>
    /// Opens a game's own settings, as the Library's gear does: the Games category, at the game.
    /// </summary>
    private void OnGameSettingsOpening(object? sender, GameRef game)
    {
        if (_libraryGames.Add(game))
        {
            GamesWork = ReadNameAsync(game);
        }

        SelectedCategory = Categories.First(category => category.Category == SettingsCategory.Games);
        ShowGames();
        SelectedGame = Games.First(row => AppSettings.GameComparer.Equals(row.Game, game));
    }

    /// <summary>
    /// Shows a game's settings, adding it to the list when it isn't there.
    /// </summary>
    private void AddGame(GameRef game)
    {
        if (!Games.Any(row => AppSettings.GameComparer.Equals(row.Game, game)))
        {
            _addedGames.Add(game);
            ShowGames();
        }

        SelectedGame = Games.First(row => AppSettings.GameComparer.Equals(row.Game, game));
    }

    /// <summary>
    /// The game a user typed: its site and key, such as nexus/stardewvalley, or its key alone, which is the game of that
    /// key listed here when only one site's is, or the game of the only site that has games. Otherwise the user chooses
    /// the site. Null when they don't, or no site has games.
    /// </summary>
    private async Task<GameRef?> GameTypedAsync(string typed, CancellationToken cancellationToken)
    {
        if (GameRef.TryParse(typed, out GameRef? named))
        {
            return named;
        }

        SourceId[] listed =
        [
            .. _libraryGames.Concat(_shown.Games.Keys).Concat(_addedGames)
                .Where(game => string.Equals(game.Key, typed, StringComparison.OrdinalIgnoreCase))
                .Select(game => game.Source)
                .Distinct(),
        ];
        SourceId[] sites = listed.Length > 0 ? listed : [.. _sources.All<IModCatalog>().Select(source => source.Id)];
        switch (sites.Length)
        {
            case 0:
                return null;
            case 1:
                return new GameRef(sites[0], typed);
            default:
                string[] names = [.. sites.Select(_sources.SiteName)];
                int? chosen = await _dialogs.ChooseAsync(
                    Strings.SettingsAddGameSiteTitle,
                    Text.Format(Strings.SettingsAddGameSiteMessage, typed),
                    names,
                    cancellationToken);
                return chosen is { } index ? new GameRef(sites[index], typed) : null;
        }
    }

    /// <summary>
    /// Asks, saying what changes, then gives <paramref name="game"/> the settings of <paramref name="other"/>, the same
    /// game on another site.
    /// </summary>
    private async Task CopyGameAsync(GameRef other, GameRef game)
    {
        GameSettings copied = _shown.GameFor(other);
        string[] what =
        [
            .. new (bool Set, string Name)[]
            {
                (copied.Folders is not null, Strings.GameCopyFolders),
                (copied.OldVersions is not null, Strings.GameCopyOldVersions),
                (copied.CheckUpdates is not null || copied.UpdateInterval is not null, Strings.GameCopyUpdates),
                (copied.Notify is not null, Strings.GameCopyNotices),
                (copied.Browse is not null, Strings.GameCopyBrowse),
                (copied.Hidden == true, Strings.GameCopyHidden),
            }.Where(setting => setting.Set).Select(setting => setting.Name),
        ];
        string message = what.Length == 0
            ? Text.Format(Strings.GameCopyMessageNone, NameWithSite(game), NameWithSite(other))
            : Text.Format(
                Strings.GameCopyMessage,
                NameWithSite(game),
                NameWithSite(other),
                what.Length == 1 ? what[0] : Text.Format(Strings.ListAnd, string.Join(", ", what[..^1]), what[^1]));
        if (await _dialogs.ConfirmAsync(Strings.GameCopyTitle, message, Strings.GameCopyConfirm, CancellationToken.None))
        {
            await ChangeAsync(token => _settings.CopyGameAsync(other, game, token), sayProblem: true);
        }
    }

    /// <summary>
    /// Lists the library's games, then asks their sites for the names not known yet.
    /// </summary>
    private async Task ReadGamesAsync()
    {
        IReadOnlyList<LibraryMod> mods;
        try
        {
            mods = await _library.ListAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            LogGamesUnreadable(_logger, exception);
            return;
        }

        List<GameRef> unnamed = [];
        foreach (GameRef game in mods.Select(mod => mod.Mod.Game).Distinct())
        {
            if (_libraryGames.Add(game))
            {
                unnamed.Add(game);
            }
        }

        ShowGames();
        await Task.WhenAll(unnamed.Select(ReadNameAsync));
    }

    /// <summary>
    /// Asks a game's site for its name and its picture, then shows them.
    /// </summary>
    private async Task ReadNameAsync(GameRef game)
    {
        if (await GameNames.ReadAsync(_sources, game, _logger, CancellationToken.None) is { } info)
        {
            _gameInfos[game] = info;
            ShowGames();
        }
    }

    /// <summary>
    /// Lists the games by name, each with its picture once its site gives one, and saying whether it has settings of its
    /// own, and shows the chosen one's, or the first's.
    /// </summary>
    private void ShowGames()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        List<GameRowViewModel> rows = [];
        foreach (GameRef game in ListedGames())
        {
            GameRowViewModel row = Games.FirstOrDefault(shown => AppSettings.GameComparer.Equals(shown.Game, game))
                ?? NewRow(game);
            row.Name = NameOf(game);
            row.Cover.Place(_gameInfos.GetValueOrDefault(game)?.CoverUrl, CancellationToken.None);
            row.Note = NoteOf(_shown.GameFor(game));
            rows.Add(row);
        }

        Games.SyncWith([.. rows.OrderBy(row => row.Name, byName)]);
        HasNoGames = Games.Count == 0;
        if (Game is { } page)
        {
            page.Name = NameOf(page.Game);
            page.Show(_shown, LibraryRoot);
            ShowSameNamed(page);
        }

        if (SelectedGame is null || !Games.Contains(SelectedGame))
        {
            SelectedGame = Games.FirstOrDefault();
        }
    }

    /// <summary>
    /// Says on <paramref name="page"/> which other sites have a game of its name, and offers each one's settings.
    /// </summary>
    private void ShowSameNamed(GameSettingsViewModel page)
    {
        IReadOnlyList<GameRef> others = _gameInfos.TryGetValue(page.Game, out GameInfo? info)
            ? SameNamedGames.Of(page.Game, info.Name, NamedGames())
            : [];
        page.ShowSameNamed(
            [.. others.Select(other => KeyValuePair.Create(other, NameWithSite(other)))],
            SameNamedGames.AlsoOn(others, _sources));
    }

    /// <summary>
    /// The games listed: the library's, those with settings of their own, and those added.
    /// </summary>
    private IEnumerable<GameRef> ListedGames()
        => _libraryGames.Concat(_shown.Games.Keys).Concat(_addedGames).Distinct(AppSettings.GameComparer);

    /// <summary>
    /// The games listed whose sites named them, with their names.
    /// </summary>
    private IEnumerable<KeyValuePair<GameRef, string>> NamedGames()
        => ListedGames().Where(_gameInfos.ContainsKey).Select(game => KeyValuePair.Create(game, _gameInfos[game].Name));

    /// <summary>
    /// The game's name once its site says it, or its key; with its site's when another site's game listed has that name.
    /// </summary>
    private string NameOf(GameRef game)
    {
        if (!_gameInfos.TryGetValue(game, out GameInfo? info))
        {
            return game.Key;
        }

        return SameNamedGames.Of(game, info.Name, NamedGames()).Count > 0 ? NameWithSite(game) : info.Name;
    }

    /// <summary>
    /// Such as "Stardew Valley (Example Mods)".
    /// </summary>
    private string NameWithSite(GameRef game)
        => Text.Format(Strings.BrowseGameOnSite, _gameInfos.GetValueOrDefault(game)?.Name ?? game.Key, _sources.SiteName(game.Source));

    private GameRowViewModel NewRow(GameRef game)
    {
        GameRowViewModel row = new(game, NameOf(game), _covers);
        row.Cover.Show(CancellationToken.None);
        return row;
    }
}
