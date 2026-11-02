using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The mods the library has files of or watches, by game, as one list of games' headings and their mods, so it can be
/// virtualized, or as cards under each game's heading. A box filters the mods by name. With previews, each mod shows its
/// picture, from its saved details. It follows the library's changes while the app runs.
/// </summary>
public sealed partial class LibraryViewModel : ScreenViewModel
{
    private readonly IModLibrary _library;
    private readonly ISourceRegistry _sources;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly LibraryServices _services;
    private readonly Dictionary<ModRef, LibraryModViewModel> _mods = [];
    private readonly Dictionary<GameRef, LibraryGameViewModel> _games = [];
    private CancellationToken _stopping;
    private bool _picturesShown;

    public LibraryViewModel(
        IModLibrary library,
        ILibraryRescan rescan,
        ISettingsService settings,
        IBrowserLauncher browser,
        IFolderOpener folders,
        IDialogService dialogs,
        ISourceRegistry sources,
        ModPictures pictures,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<LibraryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _sources = sources;
        _pictures = pictures;
        _uiThread = uiThread;
        _logger = logger;
        _services = new LibraryServices(library, rescan, settings, browser, folders, dialogs, pictures, timeProvider, logger);
        Previews = new PreviewsViewModel(PreviewScreen.Library, pictures);
        Previews.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PreviewsViewModel.Level))
            {
                ShowPictures();
            }
        };
    }

    public override Screen Screen => Screen.Library;

    public override string Title => Strings.LibraryTitle;

    /// <summary>
    /// What the mods' names must have to show; empty for every mod.
    /// </summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>
    /// Whether the mods show as rows, with thumbnails or not, or as cards.
    /// </summary>
    public PreviewsViewModel Previews { get; }

    /// <summary>
    /// Each game's heading followed by its mods, games and mods by name.
    /// </summary>
    public ObservableCollection<ObservableObject> Rows { get; } = [];

    /// <summary>
    /// The games' headings in <see cref="Rows"/>, each with its mods, for cards.
    /// </summary>
    public ObservableCollection<LibraryGameViewModel> Games { get; } = [];

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
    /// Reads the previews from the settings, lists the library, then follows its changes while the app runs, and asks the
    /// games' sites for their names.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = cancellationToken;
        await _pictures.LoadAsync();
        Previews.Refresh();
        ShowPictures();
        _pictures.Changed += (_, _) => _uiThread.Post(() =>
        {
            Previews.Refresh();
            ShowPictures();
        });
        await LibraryFeed.FollowAsync(
            _library,
            _uiThread,
            mods =>
            {
                foreach (LibraryMod mod in mods)
                {
                    Show(mod);
                }

                Arrange();
            },
            Apply,
            cancellationToken);
    }

    partial void OnFilterChanged(string value) => Arrange();

    /// <summary>
    /// Rescans every mod's folders, then says what it found. The screen shows what it added as the library changes.
    /// </summary>
    [RelayCommand]
    private Task RescanAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            () => Rescans.RunAsync(_services, null, cancellationToken),
            _services.Dialogs,
            exception => LogRescanFailed(_logger, exception),
            cancellationToken);

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
            LibraryModViewModel added = new(mod, _services);
            _mods.Add(mod.Mod, added);
            if (_picturesShown)
            {
                added.Picture.Show(mod.Mod, _stopping);
            }
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
            heading.Mods.SyncWith(mods);
            rows.Add(heading);
            rows.AddRange(mods);
        }

        Rows.SyncWith(rows);
        Games.SyncWith([.. rows.OfType<LibraryGameViewModel>()]);
        IsEmpty = _mods.Count == 0;
        NothingMatches = !IsEmpty && rows.Count == 0;
        NothingMatchesText = NothingMatches ? Text.Format(Strings.LibraryNothingMatches, filter) : string.Empty;
    }

    /// <summary>
    /// Shows each mod's picture while the screen shows pictures, as adult mods' pictures should look now; hides them
    /// otherwise. A picture already shown isn't read again.
    /// </summary>
    private void ShowPictures()
    {
        bool shown = Previews.ShowsPictures;
        foreach (LibraryModViewModel mod in _mods.Values)
        {
            if (!shown)
            {
                mod.Picture.Hide();
            }
            else if (_picturesShown)
            {
                mod.Picture.Refresh(_stopping);
            }
            else
            {
                mod.Picture.Show(mod.Mod, _stopping);
            }
        }

        _picturesShown = shown;
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
