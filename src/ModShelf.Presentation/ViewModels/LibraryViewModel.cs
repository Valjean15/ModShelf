using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The mods the library has files of or watches, by game, as one list of games' headings and their mods, so it can be
/// virtualized. A box filters the mods by name. It follows the library's changes while the app runs.
/// </summary>
public sealed partial class LibraryViewModel : ScreenViewModel
{
    private readonly IModLibrary _library;
    private readonly ISourceRegistry _sources;
    private readonly IUiThread _uiThread;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly LibraryServices _services;
    private readonly Dictionary<ModRef, LibraryModViewModel> _mods = [];
    private readonly Dictionary<GameRef, LibraryGameViewModel> _games = [];
    private CancellationToken _stopping;

    public LibraryViewModel(
        IModLibrary library,
        ISettingsService settings,
        IBrowserLauncher browser,
        IFolderOpener folders,
        IDialogService dialogs,
        ISourceRegistry sources,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<LibraryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _sources = sources;
        _uiThread = uiThread;
        _logger = logger;
        _services = new LibraryServices(library, settings, browser, folders, dialogs, timeProvider, logger);
    }

    public override Screen Screen => Screen.Library;

    public override string Title => Strings.LibraryTitle;

    /// <summary>
    /// What the mods' names must have to show; empty for every mod.
    /// </summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>
    /// Each game's heading followed by its mods, games and mods by name.
    /// </summary>
    public ObservableCollection<ObservableObject> Rows { get; } = [];

    /// <summary>
    /// Whether the library has no mods at all.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    /// <summary>
    /// Whether the library has mods, but the filter hides them all; <see cref="NothingMatchesText"/> says so.
    /// </summary>
    [ObservableProperty]
    public partial bool NothingMatches { get; private set; }

    [ObservableProperty]
    public partial string NothingMatchesText { get; private set; } = string.Empty;

    /// <summary>
    /// Lists the library, then follows its changes while the app runs, and asks the games' sites for their names.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;

        // Watching first and listing after, so no change from here on is missed.
        IAsyncEnumerable<LibraryChange> changes = _library.ChangesAsync(cancellationToken);
        IReadOnlyList<LibraryMod> mods = await _library.ListAsync(cancellationToken);
        _uiThread.Post(() =>
        {
            foreach (LibraryMod mod in mods)
            {
                Show(mod);
            }

            Arrange();
        });

        await foreach (LibraryChange change in changes)
        {
            _uiThread.Post(() => Apply(change));
        }
    }

    partial void OnFilterChanged(string value) => Arrange();

    /// <summary>
    /// Shows what a change did. Runs on the UI thread.
    /// </summary>
    private void Apply(LibraryChange change)
    {
        if (change.After is { } after)
        {
            Show(after);
        }
        else
        {
            _mods.Remove(change.Mod);
        }

        Arrange();
    }

    /// <summary>
    /// Shows the mod as it is now, adding its row, and its game's heading, when they're new. A game's heading is kept
    /// once made, so its site is asked for its name once.
    /// </summary>
    private void Show(LibraryMod mod)
    {
        if (_mods.TryGetValue(mod.Mod, out LibraryModViewModel? shown))
        {
            shown.Show(mod);
        }
        else
        {
            _mods.Add(mod.Mod, new LibraryModViewModel(mod, _services));
        }

        if (!_games.ContainsKey(mod.Mod.Game))
        {
            _games.Add(mod.Mod.Game, new LibraryGameViewModel(mod.Mod.Game));
            _ = NameAsync(mod.Mod.Game);
        }
    }

    /// <summary>
    /// Puts the rows in order: games by name, each with the mods whose names match the filter, by name. A game without
    /// such mods doesn't show.
    /// </summary>
    private void Arrange()
    {
        StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        string filter = Filter.Trim();
        List<ObservableObject> rows = [];
        foreach (IGrouping<GameRef, LibraryModViewModel> game in _mods.Values
            .Where(mod => filter.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(mod.Name, filter, CompareOptions.IgnoreCase) >= 0)
            .GroupBy(mod => mod.Mod.Game)
            .OrderBy(game => _games[game.Key].Name, byName))
        {
            LibraryGameViewModel heading = _games[game.Key];
            LibraryModViewModel[] mods = [.. game.OrderBy(mod => mod.Name, byName)];
            heading.ModsText = Counts.Of(mods.Length, Strings.LibraryOneMod, Strings.LibraryMods);
            rows.Add(heading);
            rows.AddRange(mods);
        }

        Rows.SyncWith(rows);
        IsEmpty = _mods.Count == 0;
        NothingMatches = !IsEmpty && rows.Count == 0;
        NothingMatchesText = NothingMatches ? Text.Format(Strings.LibraryNothingMatches, filter) : string.Empty;
    }

    /// <summary>
    /// Asks the game's site for its name, once. Until it answers, or when it can't, the game goes by its key.
    /// </summary>
    private async Task NameAsync(GameRef game)
    {
        if (_sources.Find<IModCatalog>(game.Source) is not { } catalog)
        {
            return;
        }

        try
        {
            GameInfo info = await catalog.GetGameAsync(game, _stopping);
            _uiThread.Post(() =>
            {
                if (_games.TryGetValue(game, out LibraryGameViewModel? heading))
                {
                    heading.Name = info.Name;
                    Arrange();
                }
            });
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            // Expected without a linked account, or offline: no stack trace for it.
            LogGameNameUnknown(_logger, game.Key, game.Source.Value, exception.Message);
        }
        catch (Exception exception)
        {
            LogGameNameFailed(_logger, exception, game.Key, game.Source.Value);
        }
    }
}
