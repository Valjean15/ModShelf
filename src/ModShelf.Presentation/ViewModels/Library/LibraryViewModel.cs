using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Watchlist;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The mods the library has files of or watches, by game, as one list of games' headings and their mods, so it can be
/// virtualized, or as cards under each game's heading. A box filters the mods by name. With previews, each mod shows its
/// picture, from its saved details. A mod opens on its page in place of the list, read from its site, or from its saved
/// details when the site can't give it. It follows the library's changes while the app runs.
/// </summary>
public sealed partial class LibraryViewModel : ScreenViewModel
{
    private readonly IModLibrary _library;
    private readonly IModLookup _lookup;
    private readonly ISourceRegistry _sources;
    private readonly ModPictures _pictures;
    private readonly IUiThread _uiThread;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly LibraryServices _services;
    private readonly ModPageServices _pageServices;
    private readonly Dictionary<ModRef, LibraryModViewModel> _mods = [];
    private readonly Dictionary<ModRef, LibraryMod> _shown = [];
    private readonly Dictionary<GameRef, LibraryGameViewModel> _games = [];
    private CancellationToken _stopping;
    private CancellationTokenSource? _opening;
    private ModRef? _openedMod;
    private bool _picturesShown;

    public LibraryViewModel(
        IModLibrary library,
        IModLookup lookup,
        ILibraryRescan rescan,
        ILibraryDownloads libraryDownloads,
        ILinkDownloads links,
        IWatchlists watchlists,
        ILibraryStorage storage,
        ISettingsService settings,
        IBrowserLauncher browser,
        IFolderOpener folders,
        IClipboard clipboard,
        IDialogService dialogs,
        ISourceRegistry sources,
        ModPictures pictures,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<LibraryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _lookup = lookup;
        _sources = sources;
        _pictures = pictures;
        _uiThread = uiThread;
        _logger = logger;
        _services = new LibraryServices(library, rescan, settings, browser, folders, dialogs, pictures, OpenAsync, timeProvider, logger);
        _pageServices = new ModPageServices(
            library,
            libraryDownloads,
            rescan,
            storage,
            settings,
            new FolderDownloads(links, dialogs),
            new TrackedMods(watchlists),
            sources,
            browser,
            folders,
            clipboard,
            dialogs,
            pictures,
            timeProvider,
            logger);
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
    /// The mod opened, in place of the list; null when none is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModOpen))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial ModPageViewModel? Mod { get; private set; }

    public bool IsModOpen => Mod is not null;

    /// <summary>
    /// The name of the game of the mod opened, for the way back to the list.
    /// </summary>
    [ObservableProperty]
    public partial string OpenGameName { get; private set; } = string.Empty;

    /// <summary>
    /// Whether a mod is being read from its site, to open it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpening { get; private set; }

    /// <summary>
    /// Why the mod opened shows its saved details rather than its site's; null when it shows its site's.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string? PageProblem { get; private set; }

    /// <summary>
    /// The last opening of a mod, for tests to wait on.
    /// </summary>
    internal Task Opening { get; private set; } = Task.CompletedTask;

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
    /// Closes the mod, back to the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsModOpen))]
    private void Back()
    {
        _opening?.Cancel();
        _openedMod = null;
        Mod = null;
        PageProblem = null;
    }

    private bool CanRetry() => PageProblem is not null && _openedMod is not null;

    /// <summary>
    /// Reads the mod opened from its site again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync(CancellationToken cancellationToken) => _openedMod is { } mod ? OpenAsync(mod, cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// Opens the mod on its page, in place of the list: read from its site, or from its saved details when the site
    /// can't give it, saying why. A mod opened later drops one still being read.
    /// </summary>
    private Task OpenAsync(ModRef mod, CancellationToken cancellationToken)
    {
        Opening = OpenPageAsync(mod);
        return Opening;
    }

    private async Task OpenPageAsync(ModRef mod)
    {
        using CancellationTokenSource opening = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _opening, opening);
        dropped?.Cancel();
        CancellationToken token = opening.Token;
        IsOpening = true;
        try
        {
            ModPage page;
            string? problem = null;
            try
            {
                page = await _lookup.LookUpAsync(mod, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (OfflineReason(mod, exception) is { } reason)
            {
                page = await SavedPageAsync(mod, token);
                problem = reason;
            }

            token.ThrowIfCancellationRequested();
            ModPageViewModel opened = new(page, shown => _shown.GetValueOrDefault(shown), _pageServices, Previews, OpenAsync, _stopping)
            {
                IsOffline = problem is not null,
            };
            _openedMod = mod;
            OpenGameName = _games.TryGetValue(mod.Game, out LibraryGameViewModel? game) ? game.Name : page.Game.Name;
            Mod = opened;
            PageProblem = problem;
            await opened.LoadAsync(token);
            if (problem is null)
            {
                await opened.ShowTrackedAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Another mod was opened, the list shown again, or the app is closing.
        }
        catch (Exception exception)
        {
            LogOpenFailed(_logger, exception, mod.Id, mod.Game.Key);
            await _services.Dialogs.ShowMessageAsync(Strings.ActionFailedTitle, Strings.SomethingWentWrong, CancellationToken.None);
        }
        finally
        {
            if (ReferenceEquals(_opening, opening))
            {
                IsOpening = false;
                _opening = null;
            }
        }
    }

    /// <summary>
    /// Why the mod's site couldn't give it, to say over its saved details; null for a failure that isn't the site's.
    /// </summary>
    private string? OfflineReason(ModRef mod, Exception exception)
        => exception is ArgumentException
            ? Text.Format(Strings.ModPageOffline, _sources.SiteName(mod.Game.Source), exception.Message)
            : SiteProblems.Describe(exception, _sources.SiteName(mod.Game.Source), Strings.ModPageOffline);

    /// <summary>
    /// The mod's page from what's saved: its saved details, or the library's, with no files or changelog.
    /// </summary>
    private async Task<ModPage> SavedPageAsync(ModRef mod, CancellationToken cancellationToken)
    {
        ModInfo? saved = await _pictures.Details.GetSavedAsync(mod, cancellationToken);
        LibraryMod? inLibrary = _shown.GetValueOrDefault(mod);
        ModInfo details = saved ?? new ModInfo(mod, inLibrary?.Name ?? mod.Id, author: inLibrary?.Author, pageUrl: inLibrary?.PageUrl);
        string gameName = _games.TryGetValue(mod.Game, out LibraryGameViewModel? game) ? game.Name : mod.Game.Key;
        return new ModPage(new GameInfo(mod.Game, gameName), details, [], [], null);
    }

    /// <summary>
    /// Rescans every mod's folders, then says what it found. The screen shows what it added as the library changes.
    /// </summary>
    [RelayCommand]
    private Task RescanAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            () => Rescans.RunAsync(_services.Rescan, _services.Library, _services.Dialogs, null, cancellationToken),
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
            _shown.Remove(change.Mod);
        }

        Arrange();

        // Also for a change to a mod the open mod needs.
        Mod?.ShowLibrary();
    }

    /// <summary>
    /// Shows the mod as it is now, adding its row, and its game's heading, when they're new. A game's heading is kept
    /// once made, so its site is asked for its name once.
    /// </summary>
    private void Show(LibraryMod mod)
    {
        _shown[mod.Mod] = mod;
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
