using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The settings, by category: General, whether the app is light or dark, what closing its window does, saved pictures
/// and mod details, and how adult mods' pictures show; Library, where the library is, moving its files when it has
/// some, how its folders are named, what happens to old versions, and rescanning it; Updates, how often to check for
/// them and whether to say so; Links, which apps open the links websites hand downloads over with; Games, single games'
/// folder patterns; and About, this ModShelf, with its own updates. A search shows the settings that match from every
/// category. Choices are saved as they're made, folder patterns when the user saves them, and each change shows the
/// settings as stored after it. The theme is applied at start and as it's chosen. The screen's badge says a newer
/// ModShelf waits for the user.
/// </summary>
public sealed partial class SettingsViewModel : ScreenViewModel
{
    // The intervals offered, besides the one set when it's another; a game's too.
    internal static readonly TimeSpan[] OfferedIntervals =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(2),
        TimeSpan.FromDays(7),
    ];

    private readonly ISettingsService _settings;
    private readonly IModLibrary _library;
    private readonly ILibraryMover _mover;
    private readonly ILibraryRescan _rescan;
    private readonly IThemeSwitcher _themes;
    private readonly ITrayHost _tray;
    private readonly IDialogService _dialogs;
    private readonly ISourceRegistry _sources;
    private readonly IHiddenGames _hiddenGames;
    private readonly ModPictures _covers;
    private readonly ILogger<SettingsViewModel> _logger;
    private AppSettings _shown = new();
    private Task _saving = Task.CompletedTask;
    private bool _showing;

    public SettingsViewModel(
        ISettingsService settings,
        IModLibrary library,
        ILibraryMover mover,
        ILibraryRescan rescan,
        ISecretStore secrets,
        IThemeSwitcher themes,
        ITrayHost tray,
        IDialogService dialogs,
        ISourceRegistry sources,
        IHiddenGames hiddenGames,
        IGameSettingsOpener gameSettings,
        PicturesViewModel pictures,
        ModPictures covers,
        LinksViewModel links,
        AboutViewModel about,
        AppUpdateViewModel appUpdate,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(rescan);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(hiddenGames);
        ArgumentNullException.ThrowIfNull(gameSettings);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(covers);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(appUpdate);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _library = library;
        _mover = mover;
        _rescan = rescan;
        _themes = themes;
        _tray = tray;
        _dialogs = dialogs;
        _sources = sources;
        _hiddenGames = hiddenGames;
        _covers = covers;
        _logger = logger;
        Pictures = pictures;
        Links = links;
        About = about;
        AppUpdate = appUpdate;
        appUpdate.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(AppUpdateViewModel.WantsUser))
            {
                ShowAppUpdateBadge();
            }
        };
        ShowAppUpdateBadge();
        KeptInText = Text.Format(Strings.SettingsKeptIn, settings.Location);
        IsStoreUnprotected = !secrets.IsProtectedByOs;
        LibraryRoot = settings.DefaultLibraryRoot;
        Folders = new FolderPatternViewModel(null, FolderPattern.Default, settings.DefaultLibraryRoot, SaveFoldersAsync);
        SelectedCategory = Categories[0];
        gameSettings.Opening += OnGameSettingsOpening;
    }

    public override Screen Screen => Screen.Settings;

    public override string Title => Strings.SettingsTitle;

    /// <summary>
    /// Pictures and mod details: the room saved pictures may take, how long mod details are kept, and how adult mods'
    /// pictures show.
    /// </summary>
    public PicturesViewModel Pictures { get; }

    /// <summary>
    /// Links from websites: which app opens the links each site hands its downloads over with.
    /// </summary>
    public LinksViewModel Links { get; }

    /// <summary>
    /// About this ModShelf: its version, its log folder and its diagnostics report.
    /// </summary>
    public AboutViewModel About { get; }

    /// <summary>
    /// ModShelf's own updates, shown with <see cref="About"/>.
    /// </summary>
    public AppUpdateViewModel AppUpdate { get; }

    /// <summary>
    /// Such as "Settings are kept in /home/jean/.local/share/ModShelf/settings.json."
    /// </summary>
    public string KeptInText { get; }

    /// <summary>
    /// Whether credentials are kept in a file encrypted for this computer, which other programs running as the user can
    /// read, because no keyring is running.
    /// </summary>
    public bool IsStoreUnprotected { get; }

    /// <summary>
    /// Whether the settings have been read, which changing them needs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseDefaultRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand))]
    public partial bool IsLoaded { get; private set; }

    /// <summary>
    /// Why the settings couldn't be read, or the last choice couldn't be saved; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The library's root folder.
    /// </summary>
    [ObservableProperty]
    public partial string LibraryRoot { get; private set; }

    /// <summary>
    /// Whether the library is in the default folder.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseDefaultRootCommand))]
    public partial bool IsDefaultRoot { get; private set; } = true;

    /// <summary>
    /// Whether the library's files are moving to another folder.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseDefaultRootCommand))]
    public partial bool IsMoving { get; private set; }

    /// <summary>
    /// What the last change of the library's folder did, or why it couldn't; null for nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? RootNote { get; private set; }

    [ObservableProperty]
    public partial bool IsRootNoteAProblem { get; private set; }

    /// <summary>
    /// The folder pattern for every game without one of its own.
    /// </summary>
    public FolderPatternViewModel Folders { get; }

    /// <summary>
    /// What happens to a file's old versions when a newer one is downloaded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeepAll))]
    [NotifyPropertyChangedFor(nameof(IsArchive))]
    [NotifyPropertyChangedFor(nameof(IsKeepLatest))]
    public partial OldVersionAction OldVersionAction { get; set; } = OldVersionRule.Default.Action;

    public bool IsKeepAll
    {
        get => OldVersionAction == OldVersionAction.KeepAll;
        set => Choose(value, OldVersionAction.KeepAll);
    }

    public bool IsArchive
    {
        get => OldVersionAction == OldVersionAction.Archive;
        set => Choose(value, OldVersionAction.Archive);
    }

    public bool IsKeepLatest
    {
        get => OldVersionAction == OldVersionAction.KeepLatest;
        set => Choose(value, OldVersionAction.KeepLatest);
    }

    /// <summary>
    /// How many versions of a file stay, the newest, when old ones are deleted: from 1 to
    /// <see cref="OldVersionRule.MaxCount"/>.
    /// </summary>
    [ObservableProperty]
    public partial int KeepCount { get; set; } = OldVersionRule.Default.Count;

    /// <summary>
    /// The most versions the rule can keep.
    /// </summary>
    public int MaxKeepCount { get; } = OldVersionRule.MaxCount;

    /// <summary>
    /// How often the watched mods can be checked for updates, shortest first.
    /// </summary>
    public ObservableCollection<IntervalChoice> IntervalChoices { get; } = [];

    [ObservableProperty]
    public partial IntervalChoice? SelectedInterval { get; set; }

    /// <summary>
    /// Whether a background check that finds new updates shows a notice.
    /// </summary>
    [ObservableProperty]
    public partial bool NotifyOfUpdates { get; set; } = true;

    /// <summary>
    /// Whether the installed app looks for a newer ModShelf when it starts.
    /// </summary>
    [ObservableProperty]
    public partial bool CheckForAppUpdates { get; set; } = true;

    /// <summary>
    /// Whether the app is light or dark.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    public partial AppTheme Theme { get; set; }

    public bool IsSystemTheme
    {
        get => Theme == AppTheme.System;
        set => Choose(value, AppTheme.System);
    }

    public bool IsLightTheme
    {
        get => Theme == AppTheme.Light;
        set => Choose(value, AppTheme.Light);
    }

    public bool IsDarkTheme
    {
        get => Theme == AppTheme.Dark;
        set => Choose(value, AppTheme.Dark);
    }

    /// <summary>
    /// Whether closing the main window keeps ModShelf running in the tray (true) or quits it (false); null to ask the
    /// next time.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskOnClose))]
    [NotifyPropertyChangedFor(nameof(IsCloseToTray))]
    [NotifyPropertyChangedFor(nameof(IsQuitOnClose))]
    public partial bool? CloseToTray { get; set; }

    public bool IsAskOnClose
    {
        get => CloseToTray is null;
        set => ChooseOnClose(value, null);
    }

    public bool IsCloseToTray
    {
        get => CloseToTray == true;
        set => ChooseOnClose(value, true);
    }

    public bool IsQuitOnClose
    {
        get => CloseToTray == false;
        set => ChooseOnClose(value, false);
    }

    /// <summary>
    /// Whether the system shows tray icons, which keeping ModShelf running with its window closed needs. Without them,
    /// closing the window quits.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTrayAvailable { get; private set; } = true;

    /// <summary>
    /// The changes saved so far, done once this is: each choice is saved in turn, and the settings shown after it.
    /// </summary>
    internal Task Saving => _saving;

    /// <summary>
    /// Reads the settings, shows them, and applies the theme; then the room saved pictures take; then asks whether the
    /// system shows tray icons, and which apps open the sites' links; then, unless the settings say not to, looks for a
    /// newer ModShelf, only to say so.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Problem = await ReloadAsync(cancellationToken);
        await Pictures.LoadAsync(cancellationToken);
        IsTrayAvailable = await _tray.IsAvailableAsync(cancellationToken);
        await Links.LoadAsync(cancellationToken);

        // Settings that can't be read leave the default, which is to look.
        if (_shown.CheckForAppUpdates ?? true)
        {
            await AppUpdate.CheckAtStartAsync(cancellationToken);
        }
    }

    partial void OnOldVersionActionChanged(OldVersionAction value) => SaveOldVersions();

    partial void OnKeepCountChanged(int value)
    {
        if (OldVersionAction == OldVersionAction.KeepLatest)
        {
            SaveOldVersions();
        }
    }

    partial void OnSelectedIntervalChanged(IntervalChoice? value)
    {
        if (value is not null)
        {
            Save(token => _settings.SetUpdateIntervalAsync(value.Interval == UpdateInterval.Default ? null : value.Interval, token));
        }
    }

    partial void OnNotifyOfUpdatesChanged(bool value) => Save(token => _settings.SetNotifyOfUpdatesAsync(value ? null : false, token));

    partial void OnCloseToTrayChanged(bool? value) => Save(token => _settings.SetCloseToTrayAsync(value, token));

    partial void OnCheckForAppUpdatesChanged(bool value) => Save(token => _settings.SetCheckForAppUpdatesAsync(value ? null : false, token));

    partial void OnThemeChanged(AppTheme value)
    {
        _themes.Apply(value);
        Save(token => _settings.SetThemeAsync(value == AppTheme.System ? null : value, token));
    }

    /// <summary>
    /// Rescans the whole library, as the Library screen's Rescan does, then says what it found.
    /// </summary>
    [RelayCommand]
    private Task RescanAsync(CancellationToken cancellationToken)
        => UserCommands.RunAsync(
            () => Rescans.RunAsync(_rescan, _library, _dialogs, null, cancellationToken),
            _dialogs,
            exception => LogRescanFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// Gives a game a folder pattern of its own, starting as every game's.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private async Task AddGameAsync(CancellationToken cancellationToken)
    {
        string? game = (await _dialogs.AskTextAsync(
                new TextQuestion(Strings.SettingsAddGameTitle, Strings.SettingsAddGameMessage, Strings.SettingsAddGameHint, Strings.SettingsAddGameConfirm),
                cancellationToken))
            ?.Trim();
        if (!string.IsNullOrEmpty(game))
        {
            AddGame(game);
        }
    }

    /// <summary>
    /// Makes a choice, from one of the radio buttons that offer it; unchecking one does nothing.
    /// </summary>
    private void Choose(bool chosen, OldVersionAction action)
    {
        if (chosen)
        {
            OldVersionAction = action;
        }
    }

    private void Choose(bool chosen, AppTheme theme)
    {
        if (chosen)
        {
            Theme = theme;
        }
    }

    private void ChooseOnClose(bool chosen, bool? closeToTray)
    {
        if (chosen)
        {
            CloseToTray = closeToTray;
        }
    }

    private void ShowAppUpdateBadge() => Badge = AppUpdate.WantsUser ? 1 : 0;

    private void SaveOldVersions()
    {
        OldVersionRule rule = OldVersionAction switch
        {
            OldVersionAction.KeepAll => OldVersionRule.KeepAll,
            OldVersionAction.Archive => OldVersionRule.Archive,
            _ => OldVersionRule.KeepLatest(Math.Clamp(KeepCount, 1, OldVersionRule.MaxCount)),
        };
        Save(token => _settings.SetOldVersionsAsync(rule == OldVersionRule.Default ? null : rule, token));
    }
}
