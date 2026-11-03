using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Browse;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One game's own settings, in the Settings screen's Games category: its folder pattern, old versions, update checks,
/// notices and how Browse starts for it, each using every game's, which it says, until it's overridden, which shows it
/// to change; and whether the lists hide the game. Each is saved as it's chosen, a folder pattern when the user saves
/// it, and the page then shows the settings as stored.
/// </summary>
public sealed partial class GameSettingsViewModel : ObservableObject
{
    private readonly Func<Func<GameSettings, GameSettings>, Task<string?>> _change;
    private AppSettings _shown = new();
    private bool _showing;

    /// <param name="game">The game as its site names it in links.</param>
    /// <param name="name">The game's name.</param>
    /// <param name="change">Changes the game's own settings, saying what went wrong, or null.</param>
    /// <param name="saveFolders">Sets or removes the game's own folder pattern, saying what went wrong, or null.</param>
    /// <param name="root">The library's folder, for the folder pattern's example.</param>
    internal GameSettingsViewModel(
        string game,
        string name,
        Func<Func<GameSettings, GameSettings>, Task<string?>> change,
        Func<string?, FolderPattern?, Task<string?>> saveFolders,
        string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(saveFolders);

        Game = game;
        Name = name;
        _change = change;
        Folders = new FolderPatternViewModel(game, FolderPattern.Default, root, saveFolders);
    }

    /// <summary>
    /// The game as its site names it in links, such as skyrimspecialedition.
    /// </summary>
    public string Game { get; }

    /// <summary>
    /// The game's name, once its site says it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideText))]
    public partial string Name { get; internal set; }

    /// <summary>
    /// What went wrong saving the last change; null when nothing did.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The last change saved, for tests to wait on.
    /// </summary>
    internal Task Saving { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Whether the game has a folder pattern of its own.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnFolders { get; private set; }

    /// <summary>
    /// Such as "Every game's: {source}/{game}/{mod} ({modid})/{file}".
    /// </summary>
    [ObservableProperty]
    public partial string EveryGamesFolders { get; private set; } = string.Empty;

    /// <summary>
    /// The game's own pattern, to change and save; its Remove uses every game's again.
    /// </summary>
    public FolderPatternViewModel Folders { get; }

    /// <summary>
    /// Whether the game has its own rule for old versions.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnOldVersions { get; private set; }

    /// <summary>
    /// Such as "Every game's: keep the newest 2".
    /// </summary>
    [ObservableProperty]
    public partial string EveryGamesOldVersions { get; private set; } = string.Empty;

    /// <summary>
    /// What the game's own rule does with old versions.
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
    /// How many versions the game's own rule keeps, when it keeps the newest.
    /// </summary>
    [ObservableProperty]
    public partial int KeepCount { get; set; } = OldVersionRule.Default.Count;

    public int MaxKeepCount { get; } = OldVersionRule.MaxCount;

    /// <summary>
    /// Whether the game says whether, or how often, its mods are checked for updates.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnChecks { get; private set; }

    /// <summary>
    /// Such as "Checked with the other games, every 6 hours."
    /// </summary>
    [ObservableProperty]
    public partial string EveryGamesChecks { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the game's watched mods are checked with every game's, as its own setting says.
    /// </summary>
    [ObservableProperty]
    public partial bool ChecksUpdates { get; set; } = true;

    /// <summary>
    /// With the other games, then the intervals offered, and the game's own when it's another.
    /// </summary>
    public ObservableCollection<GameIntervalChoice> IntervalChoices { get; } = [];

    /// <summary>
    /// How often the game's watched mods are checked.
    /// </summary>
    [ObservableProperty]
    public partial GameIntervalChoice? SelectedInterval { get; set; }

    /// <summary>
    /// Whether the game says whether a check that finds new updates of its mods shows a notice.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnNotices { get; private set; }

    /// <summary>
    /// Such as "Every game's: a notice when a check finds new updates."
    /// </summary>
    [ObservableProperty]
    public partial string EveryGamesNotices { get; private set; } = string.Empty;

    /// <summary>
    /// Whether a check that finds new updates of the game's mods shows a notice, as its own setting says.
    /// </summary>
    [ObservableProperty]
    public partial bool Notify { get; set; } = true;

    /// <summary>
    /// Whether the game says how Browse starts for it.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnBrowse { get; private set; }

    /// <summary>
    /// Such as "Browse's own: Best match · Updated any time · 20 a page".
    /// </summary>
    public string EveryGamesBrowse { get; } = Text.Format(
        Strings.GameBrowseDefault,
        BrowseChoices.Sorts[0].Name,
        BrowseChoices.TimeWindows[0].Name,
        BrowseChoices.PageSizes[0].Name);

    /// <summary>
    /// The orders Browse offers.
    /// </summary>
    public IReadOnlyList<SortChoice> Sorts { get; } = BrowseChoices.Sorts;

    [ObservableProperty]
    public partial SortChoice? SelectedSort { get; set; }

    /// <summary>
    /// How recently updated the mods Browse finds are.
    /// </summary>
    public IReadOnlyList<TimeWindowChoice> TimeWindows { get; } = BrowseChoices.TimeWindows;

    [ObservableProperty]
    public partial TimeWindowChoice? SelectedTimeWindow { get; set; }

    /// <summary>
    /// How many mods a page of Browse lists.
    /// </summary>
    public IReadOnlyList<PageSizeChoice> PageSizes { get; } = BrowseChoices.PageSizes;

    [ObservableProperty]
    public partial PageSizeChoice? SelectedPageSize { get; set; }

    /// <summary>
    /// Whether the lists leave the game out: the Library's games, Updates, and Browse's games.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHidden { get; set; }

    /// <summary>
    /// Such as "Hide Stardew Valley in the Library, Updates and Browse".
    /// </summary>
    public string HideText => Text.Format(Strings.GameHideCheck, Name);

    /// <summary>
    /// Shows the game's settings as <paramref name="settings"/> stores them, and every game's beside them.
    /// </summary>
    /// <param name="root">The library's folder, for the folder pattern's example.</param>
    internal void Show(AppSettings settings, string root)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(root);

        _showing = true;
        try
        {
            _shown = settings;
            GameSettings own = settings.GameFor(Game);
            FolderPattern everyGamesFolders = settings.LibraryFolders ?? FolderPattern.Default;
            HasOwnFolders = own.Folders is not null;
            EveryGamesFolders = Text.Format(Strings.GameEveryGames, everyGamesFolders.Text);
            Folders.Show(own.Folders ?? everyGamesFolders, root);

            OldVersionRule everyGamesRule = settings.OldVersions ?? OldVersionRule.Default;
            HasOwnOldVersions = own.OldVersions is not null;
            EveryGamesOldVersions = Text.Format(Strings.GameEveryGames, Describe(everyGamesRule));
            OldVersionRule rule = own.OldVersions ?? everyGamesRule;
            OldVersionAction = rule.Action;
            if (rule.Action == OldVersionAction.KeepLatest)
            {
                KeepCount = rule.Count;
            }

            TimeSpan everyGamesInterval = settings.UpdateInterval ?? UpdateInterval.Default;
            string every = Lower(new IntervalChoice(everyGamesInterval, markDefault: false).Label);
            string withEveryGame = Text.Format(Strings.GameIntervalEveryGames, every);
            HasOwnChecks = own.CheckUpdates is not null || own.UpdateInterval is not null;
            EveryGamesChecks = Text.Format(Strings.GameChecksDefault, every);
            ChecksUpdates = own.CheckUpdates ?? true;
            IntervalChoices.SyncWith(
            [
                IntervalChoices.FirstOrDefault(choice => choice.Interval is null && choice.Label == withEveryGame) ?? new GameIntervalChoice(null, withEveryGame),
                .. SettingsViewModel.OfferedIntervals.Concat(own.UpdateInterval is { } set ? [set] : []).Distinct().Order()
                    .Select(offered => IntervalChoices.FirstOrDefault(choice => choice.Interval == offered)
                        ?? new GameIntervalChoice(offered, new IntervalChoice(offered, markDefault: false).Label)),
            ]);
            SelectedInterval = IntervalChoices.First(choice => choice.Interval == own.UpdateInterval);

            bool everyGamesNotify = settings.NotifyOfUpdates ?? true;
            HasOwnNotices = own.Notify is not null;
            EveryGamesNotices = everyGamesNotify ? Strings.GameNoticesOnDefault : Strings.GameNoticesOffDefault;
            Notify = own.Notify ?? everyGamesNotify;

            BrowseDefaults browse = own.Browse ?? BrowseDefaults.None;
            HasOwnBrowse = own.Browse is not null;
            SelectedSort = browse.Sort is { } sort ? BrowseChoices.SortOf(sort, browse.Direction) ?? Sorts[0] : Sorts[0];
            SelectedTimeWindow = TimeWindows.FirstOrDefault(window => window.Within == browse.UpdatedWithin) ?? TimeWindows[0];
            SelectedPageSize = PageSizes.FirstOrDefault(size => size.Count == browse.PageSize) ?? PageSizes[0];

            IsHidden = own.Hidden ?? false;
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>
    /// What a rule does with old versions, in a few words, such as "keep the newest 2".
    /// </summary>
    internal static string Describe(OldVersionRule rule) => rule.Action switch
    {
        OldVersionAction.KeepAll => Strings.OldVersionsKeepAllShort,
        OldVersionAction.Archive => Strings.OldVersionsArchiveShort,
        _ => Text.Format(Strings.OldVersionsKeepLatestShort, rule.Count),
    };

    partial void OnOldVersionActionChanged(OldVersionAction value) => SaveOldVersions();

    partial void OnKeepCountChanged(int value)
    {
        if (OldVersionAction == OldVersionAction.KeepLatest)
        {
            SaveOldVersions();
        }
    }

    partial void OnChecksUpdatesChanged(bool value) => Save(own => own with { CheckUpdates = value });

    partial void OnSelectedIntervalChanged(GameIntervalChoice? value)
    {
        if (value is not null)
        {
            Save(own => own with { UpdateInterval = value.Interval, CheckUpdates = own.CheckUpdates ?? true });
        }
    }

    partial void OnNotifyChanged(bool value) => Save(own => own with { Notify = value });

    partial void OnSelectedSortChanged(SortChoice? value) => SaveBrowse();

    partial void OnSelectedTimeWindowChanged(TimeWindowChoice? value) => SaveBrowse();

    partial void OnSelectedPageSizeChanged(PageSizeChoice? value) => SaveBrowse();

    partial void OnIsHiddenChanged(bool value) => Save(own => own with { Hidden = value ? true : null });

    /// <summary>
    /// Gives the game a folder pattern of its own: every game's, to change.
    /// </summary>
    [RelayCommand]
    private async Task OverrideFoldersAsync()
        => Problem = await Folders.SaveAsAsync(_shown.LibraryFolders ?? FolderPattern.Default);

    /// <summary>
    /// Gives the game a rule of its own for old versions: every game's, to change.
    /// </summary>
    [RelayCommand]
    private Task OverrideOldVersionsAsync() => ChangeAsync(own => own with { OldVersions = _shown.OldVersions ?? OldVersionRule.Default });

    [RelayCommand]
    private Task UseDefaultOldVersionsAsync() => ChangeAsync(own => own with { OldVersions = null });

    /// <summary>
    /// Says the game's mods are checked for updates, with the other games, to change.
    /// </summary>
    [RelayCommand]
    private Task OverrideChecksAsync() => ChangeAsync(own => own with { CheckUpdates = true });

    [RelayCommand]
    private Task UseDefaultChecksAsync() => ChangeAsync(own => own with { CheckUpdates = null, UpdateInterval = null });

    /// <summary>
    /// Says whether a check shows a notice of the game's updates: as every game's do, to change.
    /// </summary>
    [RelayCommand]
    private Task OverrideNoticesAsync() => ChangeAsync(own => own with { Notify = _shown.NotifyOfUpdates ?? true });

    [RelayCommand]
    private Task UseDefaultNoticesAsync() => ChangeAsync(own => own with { Notify = null });

    /// <summary>
    /// Says how Browse starts for the game: as Browse's own does, to change.
    /// </summary>
    [RelayCommand]
    private Task OverrideBrowseAsync() => ChangeAsync(own => own with { Browse = BrowseOf(Sorts[0], TimeWindows[0], PageSizes[0]) });

    [RelayCommand]
    private Task UseDefaultBrowseAsync() => ChangeAsync(own => own with { Browse = null });

    private static BrowseDefaults BrowseOf(SortChoice sort, TimeWindowChoice window, PageSizeChoice size)
        => new() { Sort = sort.Sort, Direction = sort.Direction, UpdatedWithin = window.Within, PageSize = size.Count };

    /// <summary>
    /// Every game's interval in a sentence, such as "every 6 hours" from "Every 6 hours".
    /// </summary>
    private static string Lower(string label) => label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];

    private void Choose(bool chosen, OldVersionAction action)
    {
        if (chosen)
        {
            OldVersionAction = action;
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
        Save(own => own with { OldVersions = rule });
    }

    private void SaveBrowse()
    {
        if (SelectedSort is { } sort && SelectedTimeWindow is { } window && SelectedPageSize is { } size)
        {
            Save(own => own with { Browse = BrowseOf(sort, window, size) });
        }
    }

    /// <summary>
    /// Saves a change the user made, but not one the page made showing the settings.
    /// </summary>
    private void Save(Func<GameSettings, GameSettings> change)
    {
        if (!_showing)
        {
            Saving = ChangeAsync(change);
        }
    }

    private async Task ChangeAsync(Func<GameSettings, GameSettings> change) => Problem = await _change(change);
}
