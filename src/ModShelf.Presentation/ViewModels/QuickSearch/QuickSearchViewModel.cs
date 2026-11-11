using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.State;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.QuickSearch;

/// <summary>
/// Quick search, over any screen (Ctrl+K, or the field at the top of the menu): with nothing typed, the recent searches
/// and the screens; once something is, as <see cref="NameQuery"/> reads it, the games, the library's mods and the
/// screens whose names have it, then the settings that say it. A game opens in the Library when the library has its
/// mods, or else in Browse; a mod opens its page; a setting its category, or a game's own settings. ↓ and ↑ move from row
/// to row, Enter opens the one highlighted, and Escape closes it. A search that opened something is remembered, the
/// newest first.
/// </summary>
public sealed partial class QuickSearchViewModel : ObservableObject
{
    /// <summary>
    /// How many games are listed at most.
    /// </summary>
    internal const int MostGames = 6;

    /// <summary>
    /// How many mods are listed at most.
    /// </summary>
    internal const int MostMods = 8;

    /// <summary>
    /// How many settings are listed at most.
    /// </summary>
    internal const int MostSettings = 6;

    private readonly IModLibrary _library;
    private readonly IModLookup _lookup;
    private readonly ISourceRegistry _sources;
    private readonly IHiddenGames _hiddenGames;
    private readonly ISettingsIndex _settings;
    private readonly IScreenOpener _screens;
    private readonly IGameSettingsOpener _gameSettings;
    private readonly INavigationService _navigation;
    private readonly IUiState _uiState;
    private readonly ModPictures _pictures;
    private readonly ILogger<QuickSearchViewModel> _logger;
    private readonly Dictionary<GameRef, GameInfo> _named = [];
    private readonly GamesByName _byName;

    // Which asking of the sites that find games by name is the latest: an answer to an earlier one is dropped.
    private int _asked;
    private readonly Dictionary<GameRef, GamePictureViewModel> _covers = [];
    private IReadOnlyList<LibraryMod> _mods = [];
    private IReadOnlyList<string> _recent = [];
    private bool _recentRead;

    public QuickSearchViewModel(
        IModLibrary library,
        IModLookup lookup,
        ISourceRegistry sources,
        IHiddenGames hiddenGames,
        ISettingsIndex settings,
        IScreenOpener screens,
        IGameSettingsOpener gameSettings,
        INavigationService navigation,
        IUiState uiState,
        ModPictures pictures,
        ILogger<QuickSearchViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(hiddenGames);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(gameSettings);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiState);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _lookup = lookup;
        _sources = sources;
        _hiddenGames = hiddenGames;
        _settings = settings;
        _screens = screens;
        _gameSettings = gameSettings;
        _navigation = navigation;
        _uiState = uiState;
        _pictures = pictures;
        _logger = logger;
        _byName = new GamesByName(lookup, lookup.SearchableSources, logger, hiddenGames.IsHidden);
    }

    /// <summary>
    /// The keys that open quick search: Ctrl+K, or ⌘K on macOS.
    /// </summary>
    public string Keys { get; } = OperatingSystem.IsMacOS() ? Strings.QuickSearchShortcutMac : Strings.QuickSearchShortcut;

    /// <summary>
    /// Such as "Quick search (Ctrl+K)", for the field that opens it.
    /// </summary>
    public string Tip => Text.Format(Strings.QuickSearchTip, Keys);

    /// <summary>
    /// Whether the box shows. Closing it forgets what was typed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    /// <summary>
    /// What's typed.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>
    /// The rows found, each group's first with its heading: the recent searches and the screens while nothing's typed;
    /// then the games, the mods, the screens and the settings.
    /// </summary>
    public ObservableCollection<QuickSearchEntryViewModel> Entries { get; } = [];

    /// <summary>
    /// The row Enter opens; null when none is listed.
    /// </summary>
    [ObservableProperty]
    public partial QuickSearchEntryViewModel? Highlighted { get; private set; }

    /// <summary>
    /// Such as "Nothing has “zzz”."; null while something does, nothing's typed, or games are being found by name.
    /// </summary>
    [ObservableProperty]
    public partial string? NothingFound { get; private set; }

    /// <summary>
    /// Such as "Finding games on GameBanana…", while the sites that find their games by name are asked; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? FindingText { get; private set; }

    /// <summary>
    /// The asking of the sites that find their games by name for what's typed, for tests to wait on.
    /// </summary>
    internal Task Finding { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reading the library and the sites' games as the box opens, for tests to wait on.
    /// </summary>
    internal Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Remembering the last search, for tests to wait on.
    /// </summary>
    internal Task Remembering { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Opens the box, empty, and reads the library and the sites' games afresh; the recent searches the first time.
    /// </summary>
    [RelayCommand]
    private void Open()
    {
        Highlight(null);
        Query = string.Empty;
        IsOpen = true;
        Reading = ReadAsync();
    }

    /// <summary>
    /// Closes the box, forgetting what was typed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>
    /// Opens the row highlighted, as Enter does.
    /// </summary>
    [RelayCommand]
    private void Choose() => Highlighted?.OpenCommand.Execute(null);

    partial void OnQueryChanged(string value)
    {
        Find(value);
        Arrange();
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            Query = string.Empty;
        }
    }

    private async Task ReadAsync()
    {
        if (!_recentRead)
        {
            // Read once: only quick search changes them, so from then on they're kept here, a search showing among them
            // before it's saved.
            _recentRead = true;
            IReadOnlyList<string> saved = (await _uiState.GetAsync(CancellationToken.None)).QuickSearches;
            _recent = (UiState.None with { QuickSearches = [.. _recent, .. saved] }).QuickSearches;
        }

        await _pictures.LoadAsync();
        try
        {
            _mods = await _library.ListAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReadFailed(_logger, exception);
        }

        Arrange();
        foreach (SourceId source in _lookup.SearchableSources)
        {
            try
            {
                IEnumerable<GameInfo> games = _byName.Finds(source)
                    ? await LibraryGamesAsync(source)
                    : await _lookup.GetGamesAsync(source, CancellationToken.None);
                foreach (GameInfo game in games)
                {
                    _named[game.Game] = game;
                }
            }
            catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
            {
                // Without an account, or offline: the library's games go by their keys.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReadFailed(_logger, exception);
            }
        }

        Arrange();
    }

    /// <summary>
    /// Lists what's found for what's typed, the row highlighted staying while it's listed, or else the first.
    /// </summary>
    private void Arrange()
    {
        NameQuery query = NameQuery.Parse(Query);
        List<QuickSearchEntryViewModel> listed = [];
        if (query.IsEmpty)
        {
            AddGroup(listed, Strings.QuickSearchRecent, _recent.Select(text => Entry(QuickSearchKind.Recent, text, null, null, () => Query = text)));
            AddGroup(listed, Strings.QuickSearchScreens, ScreenEntries());
        }
        else
        {
            AddGroup(listed, Strings.QuickSearchGames, GamesFound(query));
            AddGroup(listed, Strings.QuickSearchMods, ModsFound(query));
            AddGroup(listed, Strings.QuickSearchScreens, ScreenEntries().Where(entry => query.Matches(entry.Name)));
            AddGroup(listed, Strings.QuickSearchSettings, SettingsFound(query));
        }

        string? highlighted = Highlighted is { } shown ? KeyOf(shown) : null;
        Entries.SyncWith(listed);
        Highlight(listed.FirstOrDefault(entry => KeyOf(entry) == highlighted) ?? listed.FirstOrDefault());
        NothingFound = !query.IsEmpty && listed.Count == 0 && FindingText is null ? Text.Format(Strings.QuickSearchNothingFound, query.Text) : null;
    }

    /// <summary>
    /// The library's games of a site that finds its games by name, as it names them, but those named already; the
    /// others are found as their names are typed.
    /// </summary>
    private async Task<IReadOnlyList<GameInfo>> LibraryGamesAsync(SourceId source)
    {
        GameRef[] games = [.. _mods.Select(mod => mod.Mod.Game).Where(game => game.Source == source && !_named.ContainsKey(game)).Distinct()];
        GameInfo?[] named = await Task.WhenAll(games.Select(game => GameNames.ReadAsync(_sources, game, _logger, CancellationToken.None)));
        return [.. named.OfType<GameInfo>()];
    }

    /// <summary>
    /// Asks the sites that find their games by name for what's typed, in place of what was asked before, whose answer is
    /// then dropped; the games found stay named.
    /// </summary>
    private void Find(string typed)
    {
        int asked = ++_asked;
        if (_byName.Sites.Count == 0 || NameQuery.Parse(typed).IsEmpty)
        {
            FindingText = null;
            Finding = Task.CompletedTask;
            return;
        }

        FindingText = Text.Format(Strings.GamesFindingByName, Text.List(_byName.Sites.Select(_sources.SiteName)));
        Finding = FindAsync(typed, asked);
    }

    private async Task FindAsync(string typed, int asked)
    {
        IReadOnlyList<GameInfo> found = await _byName.FindAsync(typed, only: null, CancellationToken.None);
        if (asked != _asked)
        {
            // More was typed, and is being found.
            return;
        }

        foreach (GameInfo game in found)
        {
            _named.TryAdd(game.Game, game);
        }

        FindingText = null;
        Arrange();
    }

    /// <summary>
    /// The library's games, the one with the most mods first, then the sites' other games, those whose names start with
    /// what's typed first in each, but hidden ones.
    /// </summary>
    private IEnumerable<QuickSearchEntryViewModel> GamesFound(NameQuery query)
    {
        (GameRef Game, string Name, int Mods)[] yours =
        [
            .. Visible(_mods)
                .GroupBy(mod => mod.Mod.Game)
                .Select(game => (game.Key, NameOf(game.Key), game.Count()))
                .OrderByDescending(game => game.Item3),
        ];
        HashSet<GameRef> inLibrary = [.. yours.Select(game => game.Game)];
        IEnumerable<QuickSearchEntryViewModel> library = query.Find(yours, game => game.Name).Select(game => Entry(
            QuickSearchKind.Game,
            game.Name,
            Text.Format(Strings.QuickSearchInLibrary, Counts.Of(game.Mods, Strings.QuickSearchOneMod, Strings.QuickSearchModCount)),
            Strings.QuickSearchOpenInLibrary,
            () => _screens.OpenInLibrary(game.Game),
            game.Game));
        IEnumerable<QuickSearchEntryViewModel> others = query
            .Find(_named.Values.Where(game => !inLibrary.Contains(game.Game) && !_hiddenGames.IsHidden(game.Game)), game => game.Name)
            .Select(game => Entry(
                QuickSearchKind.Game,
                game.Name,
                Text.Format(Strings.QuickSearchOnSite, _sources.SiteName(game.Game.Source)),
                Strings.QuickSearchOpenInBrowse,
                () => _screens.OpenInBrowse(game.Game),
                game.Game));
        return library.Concat(others).Take(MostGames);
    }

    /// <summary>
    /// The library's mods, but hidden games', those whose names start with what's typed first.
    /// </summary>
    private IEnumerable<QuickSearchEntryViewModel> ModsFound(NameQuery query)
        => query.Find(Visible(_mods), mod => mod.Name).Take(MostMods).Select(mod => Entry(
            QuickSearchKind.Mod,
            mod.Name,
            mod.OfferedUpdates.Count > 0 ? Text.Format(Strings.QuickSearchModState, NameOf(mod.Mod.Game), Strings.QuickSearchUpdateFound)
                : mod.IsPinned ? Text.Format(Strings.QuickSearchModState, NameOf(mod.Mod.Game), Strings.QuickSearchPinned)
                : NameOf(mod.Mod.Game),
            null,
            () => _screens.OpenMod(mod.Mod),
            colourKey: mod.Mod.Game.Key));

    /// <summary>
    /// The settings that say what's typed, then the library's games whose own settings are, by their names.
    /// </summary>
    private IEnumerable<QuickSearchEntryViewModel> SettingsFound(NameQuery query)
    {
        IEnumerable<QuickSearchEntryViewModel> parts = _settings.Entries
            .Where(entry => query.Matches(entry.Words))
            .Select(entry => Entry(
                QuickSearchKind.Setting,
                Text.Format(Strings.QuickSearchSettingPath, entry.CategoryName, entry.Heading),
                null,
                null,
                () => _screens.OpenSettings(entry.Category)));
        IEnumerable<QuickSearchEntryViewModel> games = query
            .Find(Visible(_mods).Select(mod => mod.Mod.Game).Distinct().Select(game => (Game: game, Name: NameOf(game))), game => game.Name)
            .Select(game => Entry(
                QuickSearchKind.Setting,
                Text.Format(Strings.QuickSearchSettingPath, Strings.SettingsCategoryGames, game.Name),
                null,
                null,
                () => _gameSettings.Open(game.Game)));
        return parts.Concat(games).Take(MostSettings);
    }

    private IEnumerable<QuickSearchEntryViewModel> ScreenEntries()
        => new (Screen Screen, string Name)[]
        {
            (Screen.Library, Strings.LibraryTitle),
            (Screen.Updates, Strings.UpdatesTitle),
            (Screen.Browse, Strings.BrowseTitle),
            (Screen.Downloads, Strings.DownloadsTitle),
            (Screen.Accounts, Strings.AccountsTitle),
            (Screen.Settings, Strings.SettingsTitle),
        }.Select(screen => Entry(QuickSearchKind.Screen, screen.Name, null, null, () => _navigation.NavigateTo(screen.Screen), screen: screen.Screen));

    private IEnumerable<LibraryMod> Visible(IEnumerable<LibraryMod> mods) => mods.Where(mod => !_hiddenGames.IsHidden(mod.Mod.Game));

    /// <summary>
    /// The game's name as its site lists it; its key when the site doesn't, or hasn't said yet.
    /// </summary>
    private string NameOf(GameRef game) => _named.TryGetValue(game, out GameInfo? named) ? named.Name : game.Key;

    private QuickSearchEntryViewModel Entry(
        QuickSearchKind kind, string name, string? meta, string? action, Action open, GameRef? game = null, string? colourKey = null, Screen? screen = null)
        => new(kind, name, meta, action, entry => Opened(entry, open), game is null ? null : CoverOf(game), colourKey ?? game?.Key, screen);

    /// <summary>
    /// The game's cover, as the Library shows it: its picture from its site's list while the Library shows pictures, read
    /// once; otherwise its initial on its colour.
    /// </summary>
    private GamePictureViewModel CoverOf(GameRef game)
    {
        if (!_covers.TryGetValue(game, out GamePictureViewModel? cover))
        {
            cover = new GamePictureViewModel(_pictures);
            _covers.Add(game, cover);
        }

        if (_named.TryGetValue(game, out GameInfo? named))
        {
            cover.Place(named.CoverUrl, CancellationToken.None);
        }

        if (_pictures.LevelOf(PreviewScreen.Library) == PreviewLevel.Off)
        {
            cover.Hide();
        }
        else
        {
            cover.Show(CancellationToken.None);
        }

        return cover;
    }

    /// <summary>
    /// Opens what a row stands for, closing the box and remembering what was typed; a recent search is searched again.
    /// </summary>
    private void Opened(QuickSearchEntryViewModel entry, Action open)
    {
        if (entry.Kind == QuickSearchKind.Recent)
        {
            open();
            return;
        }

        string typed = Query.Trim();
        IsOpen = false;
        open();
        if (typed.Length > 0)
        {
            _recent = (UiState.None with { QuickSearches = _recent }).WithQuickSearch(typed).QuickSearches;
            Remembering = RememberAsync(typed);
        }
    }

    private async Task RememberAsync(string typed)
    {
        try
        {
            await _uiState.ChangeAsync(state => state.WithQuickSearch(typed), CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogNotRemembered(_logger, exception);
        }
    }

    private void Move(int by)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        int at = Highlighted is { } highlighted ? Entries.IndexOf(highlighted) : -1;
        Highlight(Entries[Math.Clamp(at + by, 0, Entries.Count - 1)]);
    }

    private void Highlight(QuickSearchEntryViewModel? entry)
    {
        if (Highlighted is { } old)
        {
            old.IsHighlighted = false;
        }

        Highlighted = entry;
        if (entry is not null)
        {
            entry.IsHighlighted = true;
        }
    }

    private static void AddGroup(List<QuickSearchEntryViewModel> listed, string heading, IEnumerable<QuickSearchEntryViewModel> entries)
    {
        bool first = true;
        foreach (QuickSearchEntryViewModel entry in entries)
        {
            entry.Heading = first ? heading : null;
            first = false;
            listed.Add(entry);
        }
    }

    /// <summary>
    /// What a row stands for, kept while its rows are made again as typing changes.
    /// </summary>
    private static string KeyOf(QuickSearchEntryViewModel entry) => $"{entry.Kind}:{entry.Name}";
}
