using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The Games category: the library's games, those with settings of their own and those added, by name, and the chosen
/// one's own settings.
/// </summary>
public sealed partial class SettingsViewModel
{
    // The library's games, by key, with the source to ask their names of; their names once read; and the games added.
    private readonly Dictionary<string, GameRef> _libraryGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _gameNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addedGames = new(StringComparer.OrdinalIgnoreCase);

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

        if (string.Equals(Game?.Game, value.Game, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string game = value.Game;
        GameSettingsViewModel page = new(
            game,
            value.Name,
            change => ChangeAsync(token => _settings.SetGameAsync(game, change, token), sayProblem: false),
            SaveFoldersAsync,
            LibraryRoot);
        page.Show(_shown, LibraryRoot);
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
        if (_libraryGames.TryAdd(game.Key, game))
        {
            GamesWork = ReadNameAsync(game);
        }

        SelectedCategory = Categories.First(category => category.Category == SettingsCategory.Games);
        ShowGames();
        SelectedGame = Games.First(row => string.Equals(row.Game, game.Key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Shows a game's settings, adding it to the list when it isn't there.
    /// </summary>
    private void AddGame(string game)
    {
        if (!Games.Any(row => string.Equals(row.Game, game, StringComparison.OrdinalIgnoreCase)))
        {
            _addedGames.Add(game);
            ShowGames();
        }

        SelectedGame = Games.First(row => string.Equals(row.Game, game, StringComparison.OrdinalIgnoreCase));
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
            if (_libraryGames.TryAdd(game.Key, game))
            {
                unnamed.Add(game);
            }
        }

        ShowGames();
        await Task.WhenAll(unnamed.Select(ReadNameAsync));
    }

    /// <summary>
    /// Asks a game's site for its name, then shows it.
    /// </summary>
    private async Task ReadNameAsync(GameRef game)
    {
        if (await GameNames.ReadAsync(_sources, game, _logger, CancellationToken.None) is { } name)
        {
            _gameNames[game.Key] = name;
            ShowGames();
        }
    }

    /// <summary>
    /// Lists the games by name, each saying whether it has settings of its own, and shows the chosen one's, or the
    /// first's.
    /// </summary>
    private void ShowGames()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        List<GameRowViewModel> rows = [];
        foreach (string game in _libraryGames.Keys.Concat(_shown.Games.Keys).Concat(_addedGames).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            GameRowViewModel row = Games.FirstOrDefault(shown => string.Equals(shown.Game, game, StringComparison.OrdinalIgnoreCase))
                ?? new GameRowViewModel(game, NameOf(game));
            row.Name = NameOf(game);
            row.Note = NoteOf(_shown.GameFor(game));
            rows.Add(row);
        }

        Games.SyncWith([.. rows.OrderBy(row => row.Name, byName)]);
        HasNoGames = Games.Count == 0;
        if (Game is { } page)
        {
            page.Name = NameOf(page.Game);
            page.Show(_shown, LibraryRoot);
        }

        if (SelectedGame is null || !Games.Contains(SelectedGame))
        {
            SelectedGame = Games.FirstOrDefault();
        }
    }

    private string NameOf(string game) => _gameNames.TryGetValue(game, out string? name) ? name : game;
}
