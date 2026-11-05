using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Common;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Games;

/// <summary>
/// The game picker: a field with the chosen game as a chip, and under it, while it's open, the games in the library, the
/// one with the most mods first, then the sites' other games whose names have what's typed (<see cref="NameQuery"/>),
/// and the recent searches while nothing is. The arrow keys move from game to game, Enter chooses, and Escape closes it.
/// </summary>
public sealed partial class GamePickerViewModel : ObservableObject
{
    /// <summary>
    /// How many of the other games are listed at most; typing more of the name finds the rest.
    /// </summary>
    internal const int MostOthers = 50;

    private readonly ModPictures _pictures;
    private readonly ISourceRegistry _sources;
    private readonly Action<GameRef> _chosen;
    private readonly Action<string>? _searchAgain;
    private readonly Action<string>? _typed;
    private readonly Dictionary<GameRef, GamePickerEntryViewModel> _entries = [];
    private IReadOnlyList<GameInfo> _games = [];
    private IReadOnlyDictionary<GameRef, int> _library = new Dictionary<GameRef, int>();
    private string _sites = string.Empty;
    private GameRef? _current;

    /// <param name="chosen">What choosing a game does.</param>
    /// <param name="searchAgain">Searches a recent search again; null for a picker without recent searches.</param>
    /// <param name="typed">What Enter does with the text typed when no game is listed to choose; null for nothing.</param>
    internal GamePickerViewModel(
        ModPictures pictures, ISourceRegistry sources, Action<GameRef> chosen, Action<string>? searchAgain = null, Action<string>? typed = null)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(chosen);

        _pictures = pictures;
        _sources = sources;
        _chosen = chosen;
        _searchAgain = searchAgain;
        _typed = typed;
    }

    /// <summary>
    /// What's typed to find a game by its name. Typing opens the list.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>
    /// Whether the list shows under the field. Closing it, as clicking elsewhere does, forgets what was typed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// The game chosen, as a chip in the field; null for none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicked))]
    public partial GamePickerEntryViewModel? Picked { get; private set; }

    public bool HasPicked => Picked is not null;

    /// <summary>
    /// The game Enter chooses; null when none is listed.
    /// </summary>
    [ObservableProperty]
    public partial GamePickerEntryViewModel? Highlighted { get; private set; }

    /// <summary>
    /// The games listed: the library's, then the others found, each group's first with its heading.
    /// </summary>
    public ObservableCollection<GamePickerEntryViewModel> Entries { get; } = [];

    /// <summary>
    /// Browse's recent searches, the newest first, while nothing's typed.
    /// </summary>
    public ObservableCollection<RecentSearchViewModel> Recent { get; } = [];

    [ObservableProperty]
    public partial bool HasRecent { get; private set; }

    /// <summary>
    /// Such as "Type to find any of the 3,412 games on Nexus Mods.", while nothing's typed; null once something is.
    /// </summary>
    [ObservableProperty]
    public partial string? Hint { get; private set; }

    /// <summary>
    /// Such as "No game's name has “zzz”."; null while some game's does, nothing's typed, or there are no games to find,
    /// as when the sites couldn't list theirs.
    /// </summary>
    [ObservableProperty]
    public partial string? NothingFound { get; private set; }

    /// <summary>
    /// Such as "120 more: type more of the name.", when more of the other games have it than are listed; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? MoreText { get; private set; }

    /// <summary>
    /// How many games the picker can find, those of the library or not.
    /// </summary>
    internal int GameCount => _games.Count;

    /// <summary>
    /// Offers <paramref name="games"/>: those the sites list that can be chosen. Their sites show when they're of two
    /// sites or more.
    /// </summary>
    internal void ShowGames(IEnumerable<GameInfo> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        _games = [.. games.OrderBy(game => game.Name, byName)];
        string[] sites = [.. _games.Select(game => game.Game.Source).Distinct().Select(_sources.SiteName).Order(byName)];
        _sites = sites.Length switch
        {
            0 => string.Empty,
            1 => sites[0],
            _ => Text.Format(Strings.ListAnd, string.Join(", ", sites[..^1]), sites[^1]),
        };
        foreach (GamePickerEntryViewModel entry in _entries.Values)
        {
            entry.ShowSite = sites.Length > 1;
        }

        ShowPicked(_current);
        Arrange();
    }

    /// <summary>
    /// Says how many mods the library has of each game: those it has any of are "Your games".
    /// </summary>
    internal void ShowLibrary(IReadOnlyDictionary<GameRef, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        _library = counts;
        Arrange();
    }

    /// <summary>
    /// Offers Browse's recent searches, the newest first.
    /// </summary>
    internal void ShowRecent(IEnumerable<string> searches)
    {
        ArgumentNullException.ThrowIfNull(searches);

        Recent.Clear();
        if (_searchAgain is { } search)
        {
            foreach (string text in searches)
            {
                Recent.Add(new RecentSearchViewModel(text, typed =>
                {
                    IsOpen = false;
                    search(typed);
                }));
            }
        }

        Arrange();
    }

    /// <summary>
    /// Shows the game chosen elsewhere, as when Browse starts on the game the library has the most mods of; null for
    /// none.
    /// </summary>
    internal void ShowPicked(GameRef? game)
    {
        _current = game;
        Picked = game is null ? null : EntryFor(game);
        Picked?.Cover.Show(CancellationToken.None);
    }

    /// <summary>
    /// Opens the list, as focusing the field does: once there are games or recent searches to show.
    /// </summary>
    [RelayCommand]
    private void Open() => IsOpen = IsOpen || _games.Count > 0 || Recent.Count > 0;

    /// <summary>
    /// Closes the list, forgetting what was typed; the game chosen stays.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>
    /// Chooses the game highlighted; with none listed, hands what's typed on, where the picker takes typed text.
    /// </summary>
    [RelayCommand]
    private void Choose()
    {
        if (Highlighted is { } entry)
        {
            Pick(entry);
        }
        else if (_typed is { } typed && !string.IsNullOrWhiteSpace(Query))
        {
            typed(Query.Trim());
        }
    }

    /// <summary>
    /// Takes the chip away to choose another game: the list opens, and closing it without choosing puts the chip back.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        Picked = null;
        IsOpen = true;
    }

    partial void OnQueryChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            IsOpen = true;
        }

        Arrange();
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            Query = string.Empty;
            ShowPicked(_current);
        }

        Arrange();
    }

    private void Pick(GamePickerEntryViewModel entry)
    {
        _current = entry.Game;
        Picked = entry;
        IsOpen = false;
        _chosen(entry.Game);
    }

    private void Move(int by)
    {
        if (!IsOpen)
        {
            IsOpen = true;
            return;
        }

        if (Entries.Count == 0)
        {
            return;
        }

        int at = Highlighted is { } highlighted ? Entries.IndexOf(highlighted) : -1;
        Highlight(Entries[Math.Clamp(at + by, 0, Entries.Count - 1)]);
    }

    private void Highlight(GamePickerEntryViewModel? entry)
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

    /// <summary>
    /// Lists the library's games whose names have what's typed, the one with the most mods first, then, once something's
    /// typed, the other games that have it, those that start with it first; the game highlighted stays while listed, or
    /// else the first is.
    /// </summary>
    private void Arrange()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        NameQuery query = NameQuery.Parse(Query);
        GameInfo[] yours =
        [
            .. _games
                .Where(game => _library.ContainsKey(game.Game) && query.Matches(game.Name))
                .OrderByDescending(game => _library[game.Game])
                .ThenBy(game => game.Name, byName),
        ];
        IReadOnlyList<GameInfo> found = query.IsEmpty ? [] : query.Find(_games.Where(game => !_library.ContainsKey(game.Game)), game => game.Name);
        List<GamePickerEntryViewModel> listed = [];
        foreach (GameInfo game in yours)
        {
            GamePickerEntryViewModel entry = EntryFor(game.Game)!;
            int mods = _library[game.Game];
            entry.Meta = Counts.Of(mods, Strings.GamePickerOneMod, Strings.GamePickerMods);
            entry.Heading = listed.Count == 0 ? Strings.GamePickerYours : null;
            entry.Cover.Show(CancellationToken.None);
            listed.Add(entry);
        }

        int first = listed.Count;
        string heading = Text.Format(_library.Count > 0 ? Strings.GamePickerOthers : Strings.GamePickerAll, _sites);
        foreach (GameInfo game in found.Take(MostOthers))
        {
            GamePickerEntryViewModel entry = EntryFor(game.Game)!;
            entry.Meta = _library.Count > 0 ? Strings.GamePickerNotYours : string.Empty;
            entry.Heading = listed.Count == first ? heading : null;
            listed.Add(entry);
        }

        Entries.SyncWith(listed);
        Highlight(Highlighted is { } highlighted && listed.Contains(highlighted) ? highlighted : listed.FirstOrDefault());
        HasRecent = query.IsEmpty && Recent.Count > 0;
        Hint = query.IsEmpty && _games.Count > 0 ? Text.Format(Strings.GamePickerTypeToFind, _games.Count, _sites) : null;
        NothingFound = !query.IsEmpty && listed.Count == 0 && _games.Count > 0 ? Text.Format(Strings.GamePickerNothingFound, query.Text) : null;
        MoreText = found.Count > MostOthers ? Text.Format(Strings.GamePickerMore, found.Count - MostOthers) : null;
    }

    /// <summary>
    /// The game's entry, made once; null for a game the sites don't list.
    /// </summary>
    private GamePickerEntryViewModel? EntryFor(GameRef game)
    {
        if (_entries.TryGetValue(game, out GamePickerEntryViewModel? entry))
        {
            return entry;
        }

        if (_games.FirstOrDefault(listed => listed.Game == game) is not { } info)
        {
            return null;
        }

        entry = new GamePickerEntryViewModel(game, info.Name, _sources.SiteName(game.Source), _pictures, Pick)
        {
            ShowSite = _games.Select(listed => listed.Game.Source).Distinct().Skip(1).Any(),
        };
        entry.Cover.Place(info.CoverUrl, CancellationToken.None);
        _entries.Add(game, entry);
        return entry;
    }
}
