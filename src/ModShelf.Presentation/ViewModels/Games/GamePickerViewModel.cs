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
/// A site that finds its games by name (<see cref="GamesByName"/>) is asked as the name is typed, each answer taking the
/// last one's place; the games it found stay known, so one chosen keeps its name and cover. A site chosen
/// (<see cref="ShowSite"/>) narrows it to that site's games.
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
    private readonly GamesByName? _byName;
    private readonly Dictionary<GameRef, GamePickerEntryViewModel> _entries = [];
    private readonly Dictionary<GameRef, GameInfo> _found = [];
    private IReadOnlyList<GameInfo> _games = [];

    // Which asking of the sites that find games by name is the latest: an answer to an earlier one is dropped.
    private int _asked;
    private IReadOnlyDictionary<GameRef, int> _library = new Dictionary<GameRef, int>();
    private string _sites = string.Empty;
    private GameRef? _current;
    private SourceId? _site;

    /// <param name="chosen">What choosing a game does.</param>
    /// <param name="searchAgain">Searches a recent search again; null for a picker without recent searches.</param>
    /// <param name="typed">What Enter does with the text typed when no game is listed to choose; null for nothing.</param>
    /// <param name="byName">The sites that find their games by name, asked as it's typed; null for none.</param>
    internal GamePickerViewModel(
        ModPictures pictures,
        ISourceRegistry sources,
        Action<GameRef> chosen,
        Action<string>? searchAgain = null,
        Action<string>? typed = null,
        GamesByName? byName = null)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(chosen);

        _pictures = pictures;
        _sources = sources;
        _chosen = chosen;
        _searchAgain = searchAgain;
        _typed = typed;
        _byName = byName is { Sites.Count: > 0 } ? byName : null;
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
    /// Such as "Finding games on GameBanana…", while the sites that find their games by name are asked; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? FindingText { get; private set; }

    /// <summary>
    /// How many games the picker can find, those of the library or not, but those found by name.
    /// </summary>
    internal int GameCount => _games.Count;

    /// <summary>
    /// The asking of the sites that find their games by name for what's typed, for tests to wait on.
    /// </summary>
    internal Task Finding { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// What the picker knows of a game: listed, or found by name; null for one it doesn't.
    /// </summary>
    internal GameInfo? Known(GameRef game)
        => _games.FirstOrDefault(listed => listed.Game == game) ?? _found.GetValueOrDefault(game);

    /// <summary>
    /// Offers <paramref name="games"/>: those the sites list that can be chosen. Their sites show when they're of two
    /// sites or more.
    /// </summary>
    internal void ShowGames(IEnumerable<GameInfo> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        _games = [.. games.OrderBy(game => game.Name, byName)];
        ShowSites();
    }

    /// <summary>
    /// Offers only <paramref name="site"/>'s games, each without its site's name, and asks only it for games by name, when
    /// it finds them so; null offers every site's.
    /// </summary>
    internal void ShowSite(SourceId? site)
    {
        if (site == _site)
        {
            return;
        }

        _site = site;
        ShowSites();
        Find(Query);
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
        if (Picked is { } picked)
        {
            // A game of another site than the one chosen, as quick search opens, says its site.
            picked.ShowSite = ShowsSites || !Offers(picked.Game.Source);
            picked.Cover.Show(CancellationToken.None);
        }
    }

    /// <summary>
    /// Opens the list, as focusing the field does: once there are games or recent searches to show.
    /// </summary>
    [RelayCommand]
    private void Open() => IsOpen = IsOpen || _games.Count > 0 || Recent.Count > 0 || ByNameSites.Count > 0;

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

        Find(value);
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
        GameInfo[] offered = [.. _games.Where(game => Offers(game.Game.Source))];
        GameInfo[] yours =
        [
            .. offered
                .Where(game => _library.ContainsKey(game.Game) && query.Matches(game.Name))
                .OrderByDescending(game => _library[game.Game])
                .ThenBy(game => game.Name, byName),
        ];
        IReadOnlyList<GameInfo> found = query.IsEmpty
            ? []
            : query.Find(
                offered.Concat(_found.Values.Where(game => Offers(game.Game.Source) && !_games.Any(listed => listed.Game == game.Game)))
                    .Where(game => !_library.ContainsKey(game.Game))
                    .OrderBy(game => game.Name, byName),
                game => game.Name);
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
        Hint = query.IsEmpty ? HintText() : null;
        NothingFound = !query.IsEmpty && listed.Count == 0 && FindingText is null && (offered.Length > 0 || ByNameSites.Count > 0)
            ? Text.Format(Strings.GamePickerNothingFound, query.Text)
            : null;
        MoreText = found.Count > MostOthers ? Text.Format(Strings.GamePickerMore, found.Count - MostOthers) : null;
    }

    /// <summary>
    /// Whether the games offered are of two sites or more, so each says its site.
    /// </summary>
    private bool ShowsSites => _site is null && _games.Select(listed => listed.Game.Source).Concat(_byName?.Sites ?? []).Distinct().Skip(1).Any();

    /// <summary>
    /// The sites that find their games by name whose games are offered.
    /// </summary>
    private IReadOnlyList<SourceId> ByNameSites => [.. (_byName?.Sites ?? []).Where(Offers)];

    /// <summary>
    /// Whether the games of <paramref name="source"/> are offered: every site's, unless one is chosen.
    /// </summary>
    private bool Offers(SourceId source) => _site is not { } site || source == site;

    /// <summary>
    /// Names the sites whose games are offered, and says on each game whether it shows its site.
    /// </summary>
    private void ShowSites()
    {
        _sites = SiteNames(_games.Select(game => game.Game.Source).Concat(ByNameSites).Where(Offers));
        foreach (GamePickerEntryViewModel entry in _entries.Values)
        {
            entry.ShowSite = ShowsSites;
        }

        ShowPicked(_current);
        Arrange();
    }

    /// <summary>
    /// Such as "Type to find any of the 3,412 games on Nexus Mods.", or, with sites that find their games by name, "…, or a
    /// game on GameBanana by its name."; null when there's nothing to find.
    /// </summary>
    private string? HintText()
    {
        GameInfo[] listed = [.. _games.Where(game => Offers(game.Game.Source) && _byName?.Finds(game.Game.Source) != true)];
        string listing = SiteNames(listed.Select(game => game.Game.Source));
        string byName = SiteNames(ByNameSites);
        return (listed.Length, ByNameSites.Count > 0) switch
        {
            (0, false) => null,
            (_, false) => Text.Format(Strings.GamePickerTypeToFind, listed.Length, listing),
            (0, true) => Text.Format(Strings.GamePickerTypeToFindByName, byName),
            _ => Text.Format(Strings.GamePickerTypeToFindOrByName, listed.Length, listing, byName),
        };
    }

    /// <summary>
    /// The sites' names, in order, as "Nexus Mods and GameBanana".
    /// </summary>
    private string SiteNames(IEnumerable<SourceId> sources)
        => Text.List(sources.Distinct().Select(_sources.SiteName).Order(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true)));

    /// <summary>
    /// Asks the sites that find their games by name for what's typed, in place of what was asked before, whose answer is
    /// then dropped; the games found stay known.
    /// </summary>
    private void Find(string typed)
    {
        int asked = ++_asked;
        IReadOnlyList<SourceId> sites = ByNameSites;
        if (_byName is not { } byName || sites.Count == 0 || NameQuery.Parse(typed).IsEmpty)
        {
            FindingText = null;
            Finding = Task.CompletedTask;
            return;
        }

        FindingText = Text.Format(Strings.GamesFindingByName, SiteNames(sites));
        Finding = FindAsync(byName, typed, _site, asked);
    }

    private async Task FindAsync(GamesByName byName, string typed, SourceId? site, int asked)
    {
        IReadOnlyList<GameInfo> found = await byName.FindAsync(typed, site, CancellationToken.None);
        if (asked != _asked)
        {
            // More was typed, and is being found.
            return;
        }

        foreach (GameInfo game in found)
        {
            _found[game.Game] = game;
        }

        FindingText = null;
        Arrange();
    }

    /// <summary>
    /// The game's entry, made once; null for a game the sites don't list, and none found by name.
    /// </summary>
    private GamePickerEntryViewModel? EntryFor(GameRef game)
    {
        if (_entries.TryGetValue(game, out GamePickerEntryViewModel? entry))
        {
            return entry;
        }

        if (Known(game) is not { } info)
        {
            return null;
        }

        entry = new GamePickerEntryViewModel(game, info.Name, _sources.SiteName(game.Source), _pictures, Pick)
        {
            ShowSite = ShowsSites,
        };
        entry.Cover.Place(info.CoverUrl, CancellationToken.None);
        _entries.Add(game, entry);
        return entry;
    }
}
