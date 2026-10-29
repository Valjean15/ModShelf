using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The settings: where the library is, moving its files when it has some; how its folders are named, for every game and
/// for single games; what happens to old versions; how often to check for updates and whether to say so; whether the
/// app is light or dark; and what closing its window does. Choices are saved as they're made, folder patterns when the
/// user saves them, and each change shows the settings as stored after it. The theme is applied at start and as it's
/// chosen.
/// </summary>
public sealed partial class SettingsViewModel : ScreenViewModel
{
    // The intervals offered, besides the one set when it's another.
    private static readonly TimeSpan[] OfferedIntervals =
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
    private readonly IThemeSwitcher _themes;
    private readonly ITrayHost _tray;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SettingsViewModel> _logger;
    private AppSettings _shown = new();
    private Task _saving = Task.CompletedTask;
    private bool _showing;

    public SettingsViewModel(
        ISettingsService settings,
        IModLibrary library,
        ILibraryMover mover,
        ISecretStore secrets,
        IThemeSwitcher themes,
        ITrayHost tray,
        IDialogService dialogs,
        ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _library = library;
        _mover = mover;
        _themes = themes;
        _tray = tray;
        _dialogs = dialogs;
        _logger = logger;
        KeptInText = Text.Format(Strings.SettingsKeptIn, settings.Location);
        IsStoreUnprotected = !secrets.IsProtectedByOs;
        LibraryRoot = settings.DefaultLibraryRoot;
        Folders = new FolderPatternViewModel(null, FolderPattern.Default, settings.DefaultLibraryRoot, SaveFoldersAsync);
    }

    public override Screen Screen => Screen.Settings;

    public override string Title => Strings.SettingsTitle;

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
    /// The games with folder patterns of their own, by name.
    /// </summary>
    public ObservableCollection<FolderPatternViewModel> GameFolders { get; } = [];

    /// <summary>
    /// Why a game couldn't be added; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? GameFoldersNote { get; private set; }

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
    /// Reads the settings, shows them, and applies the theme; then asks whether the system shows tray icons.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Problem = await ReloadAsync(cancellationToken);
        IsTrayAvailable = await _tray.IsAvailableAsync(cancellationToken);
    }

    /// <summary>
    /// Whether two paths name the same folder: ignoring case on Windows and macOS, whose usual file systems do.
    /// </summary>
    private static bool SamePath(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string MessageOf(Exception exception)
        => exception is ArgumentException refused ? LinkProblems.MessageForUser(refused) : exception.Message;

    private bool CanChangeRoot() => IsLoaded && !IsMoving;

    private bool CanUseDefaultRoot() => CanChangeRoot() && !IsDefaultRoot;

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

    partial void OnThemeChanged(AppTheme value)
    {
        _themes.Apply(value);
        Save(token => _settings.SetThemeAsync(value == AppTheme.System ? null : value, token));
    }

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
        if (string.IsNullOrEmpty(game))
        {
            return;
        }

        if (_shown.GameFolders.ContainsKey(game))
        {
            GameFoldersNote = Text.Format(Strings.SettingsGameHasFolders, game);
            return;
        }

        GameFoldersNote = await SaveFoldersAsync(game, _shown.LibraryFolders ?? FolderPattern.Default);
    }

    /// <summary>
    /// Moves the library to a folder the user picks.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeRoot))]
    private async Task ChooseRootAsync(CancellationToken cancellationToken)
    {
        if (await _dialogs.PickFolderAsync(Strings.SettingsChooseFolderTitle, cancellationToken) is { } folder)
        {
            await ChangeRootAsync(folder, cancellationToken);
        }
    }

    /// <summary>
    /// Moves the library back to the default folder.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseDefaultRoot))]
    private Task UseDefaultRootAsync(CancellationToken cancellationToken) => ChangeRootAsync(null, cancellationToken);

    /// <summary>
    /// Makes <paramref name="folder"/> the library's root, or the default with null. A library with files moves them
    /// there, once the user confirms.
    /// </summary>
    private async Task ChangeRootAsync(string? folder, CancellationToken cancellationToken)
    {
        string target = folder ?? _settings.DefaultLibraryRoot;
        if (SamePath(target, LibraryRoot))
        {
            return;
        }

        RootNote = null;
        IsRootNoteAProblem = false;
        string? missing = null;
        try
        {
            int files = (await _library.ListAsync(cancellationToken)).Sum(mod => mod.Files.Count);
            if (files == 0)
            {
                await _settings.SetLibraryRootAsync(folder, cancellationToken);
            }
            else
            {
                string message = files == 1
                    ? Text.Format(Strings.SettingsMoveMessageOne, LibraryRoot, target)
                    : Text.Format(Strings.SettingsMoveMessage, files, LibraryRoot, target);
                if (!await _dialogs.ConfirmAsync(Strings.SettingsMoveTitle, message, Strings.SettingsMoveConfirm, cancellationToken))
                {
                    return;
                }

                IsMoving = true;
                LibraryMove move = await _mover.MoveAsync(target, cancellationToken);
                missing = move.Missing switch
                {
                    0 => null,
                    1 => Strings.SettingsMissingOne,
                    int count => Text.Format(Strings.SettingsMissing, count),
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing; a move that stopped part-way finishes when it's asked for again.
            return;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or InvalidDataException)
        {
            // Such as downloads into the library that aren't finished, or a file that couldn't be moved.
            RootNote = MessageOf(exception);
            IsRootNoteAProblem = true;
        }
        catch (Exception exception)
        {
            LogMoveFailed(_logger, exception);
            RootNote = Strings.SomethingWentWrong;
            IsRootNoteAProblem = true;
        }
        finally
        {
            IsMoving = false;
        }

        // Whatever happened, the root shown is the one stored.
        string? readProblem = await ReloadAsync(CancellationToken.None);
        if (!IsRootNoteAProblem)
        {
            RootNote = missing is null
                ? Text.Format(Strings.SettingsLibraryIsIn, LibraryRoot)
                : $"{Text.Format(Strings.SettingsLibraryIsIn, LibraryRoot)} {missing}";
        }

        Problem = readProblem;
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

    /// <summary>
    /// Saves a choice the user made, after the ones before it, then says what went wrong, if anything. Choices shown
    /// from the settings aren't saved back.
    /// </summary>
    private void Save(Func<CancellationToken, Task> change)
    {
        if (!_showing)
        {
            _ = ChangeAsync(change, sayProblem: true);
        }
    }

    /// <summary>
    /// Saves a game's folder pattern, or every game's with a null game; a null pattern sets it back.
    /// </summary>
    /// <returns>What went wrong; null when it's saved.</returns>
    private Task<string?> SaveFoldersAsync(string? game, FolderPattern? pattern)
        => ChangeAsync(token => _settings.SetLibraryFoldersAsync(game, pattern, token), sayProblem: false);

    /// <summary>
    /// Makes a change once the changes before it are done, then shows the settings as stored.
    /// </summary>
    /// <param name="sayProblem">Whether to show what went wrong as <see cref="Problem"/>, rather than only return it.</param>
    /// <returns>What went wrong, changing or reading the settings; null for nothing.</returns>
    private Task<string?> ChangeAsync(Func<CancellationToken, Task> change, bool sayProblem)
    {
        Task<string?> changing = ChangeAfterAsync(_saving, change, sayProblem);
        _saving = changing;
        return changing;
    }

    private async Task<string?> ChangeAfterAsync(Task before, Func<CancellationToken, Task> change, bool sayProblem)
    {
        // An earlier change, which never throws.
        await before;

        string? problem = null;
        try
        {
            await change(CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            // Such as settings that can't be read or saved.
            problem = MessageOf(exception);
        }
        catch (Exception exception)
        {
            LogSaveFailed(_logger, exception);
            problem = Strings.SomethingWentWrong;
        }

        // Whatever happened, what's shown is what's stored.
        string? readProblem = await ReloadAsync(CancellationToken.None);
        problem ??= readProblem;
        if (sayProblem)
        {
            Problem = problem;
        }

        return problem;
    }

    /// <summary>
    /// Reads the settings, and shows them.
    /// </summary>
    /// <returns>Why they couldn't be read; null when they're shown.</returns>
    private async Task<string?> ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Show(await _settings.GetAsync(cancellationToken));
            return null;
        }
        catch (InvalidDataException exception)
        {
            IsLoaded = false;
            return exception.Message;
        }
    }

    /// <summary>
    /// Shows the settings as stored, without saving them back, and applies their theme.
    /// </summary>
    private void Show(AppSettings settings)
    {
        _showing = true;
        try
        {
            _shown = settings;
            LibraryRoot = settings.LibraryRoot ?? _settings.DefaultLibraryRoot;
            IsDefaultRoot = SamePath(LibraryRoot, _settings.DefaultLibraryRoot);

            Folders.Show(settings.LibraryFolders ?? FolderPattern.Default, LibraryRoot);
            List<FolderPatternViewModel> games = [];
            foreach ((string game, FolderPattern pattern) in settings.GameFolders.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                FolderPatternViewModel row = GameFolders.FirstOrDefault(shown => string.Equals(shown.Game, game, StringComparison.OrdinalIgnoreCase))
                    ?? new FolderPatternViewModel(game, pattern, LibraryRoot, SaveFoldersAsync);
                row.Show(pattern, LibraryRoot);
                games.Add(row);
            }

            GameFolders.SyncWith(games);

            OldVersionRule rule = settings.OldVersions ?? OldVersionRule.Default;
            OldVersionAction = rule.Action;
            if (rule.Action == OldVersionAction.KeepLatest)
            {
                KeepCount = rule.Count;
            }

            TimeSpan interval = settings.UpdateInterval ?? UpdateInterval.Default;
            IntervalChoices.SyncWith(
            [
                .. OfferedIntervals.Append(interval).Distinct().Order()
                    .Select(offered => IntervalChoices.FirstOrDefault(choice => choice.Interval == offered) ?? new IntervalChoice(offered)),
            ]);
            SelectedInterval = IntervalChoices.First(choice => choice.Interval == interval);

            NotifyOfUpdates = settings.NotifyOfUpdates ?? true;
            Theme = settings.Theme ?? AppTheme.System;
            CloseToTray = settings.CloseToTray;
            IsLoaded = true;
        }
        finally
        {
            _showing = false;
        }
    }
}
