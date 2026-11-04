using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// One game's own settings, in the Settings screen's Games category: its folder pattern, old versions, update checks,
/// notices, previews and how Browse starts for it, each using every game's, or the screens', which it says, until it's
/// overridden, which shows it to change; and whether the lists hide the game. Each is saved as it's chosen, a folder pattern when the user saves
/// it, and the page then shows the settings as stored.
/// </summary>
public sealed partial class GameSettingsViewModel : ObservableObject
{
    private readonly Func<Func<GameSettings, GameSettings>, Task<string?>> _change;
    private readonly Func<GameRef, Task> _copyFrom;
    private readonly BrowseOffer _browse;
    private AppSettings _shown = new();
    private bool _showing;

    /// <param name="game">The game.</param>
    /// <param name="name">The game's name.</param>
    /// <param name="change">Changes the game's own settings, saying what went wrong, or null.</param>
    /// <param name="saveFolders">Sets or removes the game's own folder pattern, saying what went wrong, or null.</param>
    /// <param name="copyFrom">Asks, then gives the game the settings of the same game on another site.</param>
    /// <param name="browse">What Browse offers for the game's site, for how Browse starts.</param>
    /// <param name="root">The library's folder, for the folder pattern's example.</param>
    internal GameSettingsViewModel(
        GameRef game,
        string name,
        Func<Func<GameSettings, GameSettings>, Task<string?>> change,
        Func<GameRef?, FolderPattern?, Task<string?>> saveFolders,
        Func<GameRef, Task> copyFrom,
        BrowseOffer browse,
        string root)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(saveFolders);
        ArgumentNullException.ThrowIfNull(copyFrom);
        ArgumentNullException.ThrowIfNull(browse);

        Game = game;
        Name = name;
        _change = change;
        _copyFrom = copyFrom;
        _browse = browse;
        Folders = new FolderPatternViewModel(game, FolderPattern.Default, root, saveFolders);
    }

    /// <summary>
    /// The game: its site, and its key there, such as skyrimspecialedition.
    /// </summary>
    public GameRef Game { get; }

    /// <summary>
    /// The game's name, once its site says it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideText))]
    public partial string Name { get; internal set; }

    /// <summary>
    /// Such as "Also on Example Mods", when another site has a game of the same name; empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlsoOn))]
    public partial string AlsoOnText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether another site has a game of the same name.
    /// </summary>
    public bool HasAlsoOn => AlsoOnText.Length > 0;

    /// <summary>
    /// The same game on other sites, whose settings the game can take, as the user chooses.
    /// </summary>
    public ObservableCollection<GameCopyViewModel> CopyChoices { get; } = [];

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
    /// Whether the game has previews of its own, which its page in the Library and Browse show in place of the screens'.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnPreviews { get; private set; }

    /// <summary>
    /// Such as "The screens' own: thumbnails in the Library, cards in Browse."
    /// </summary>
    [ObservableProperty]
    public partial string EveryGamesPreviews { get; private set; } = string.Empty;

    /// <summary>
    /// The game's own previews.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewsOff), nameof(IsPreviewsThumbnails), nameof(IsPreviewsCards))]
    public partial PreviewLevel Previews { get; set; } = AppSettings.DefaultPreview(PreviewScreen.Library);

    public bool IsPreviewsOff
    {
        get => Previews == PreviewLevel.Off;
        set => ChoosePreviews(value, PreviewLevel.Off);
    }

    public bool IsPreviewsThumbnails
    {
        get => Previews == PreviewLevel.Thumbnails;
        set => ChoosePreviews(value, PreviewLevel.Thumbnails);
    }

    public bool IsPreviewsCards
    {
        get => Previews == PreviewLevel.Cards;
        set => ChoosePreviews(value, PreviewLevel.Cards);
    }

    /// <summary>
    /// Whether the game says how Browse starts for it.
    /// </summary>
    [ObservableProperty]
    public partial bool HasOwnBrowse { get; private set; }

    /// <summary>
    /// Such as "Browse's own: Best match · Updated any time · 20 a page".
    /// </summary>
    public string EveryGamesBrowse => Text.Format(Strings.GameBrowseDefault, _browse.Default.Name, TimeWindows[0].Name, PageSizes[0].Name);

    /// <summary>
    /// The orders Browse offers for the game's site, by approval in its words.
    /// </summary>
    public IReadOnlyList<SortChoice> Sorts => _browse.Sorts;

    [ObservableProperty]
    public partial SortChoice? SelectedSort { get; set; }

    /// <summary>
    /// How recently updated the mods Browse finds are: any time, and more where the game's site can search by it.
    /// </summary>
    public IReadOnlyList<TimeWindowChoice> TimeWindows => _browse.TimeWindows;

    /// <summary>
    /// Whether the game's site can search by when mods were updated, for the box to show.
    /// </summary>
    public bool CanSearchByUpdated => _browse.Has(SearchFilters.UpdatedSince);

    [ObservableProperty]
    public partial TimeWindowChoice? SelectedTimeWindow { get; set; }

    /// <summary>
    /// How many mods a page of Browse lists, as many as the game's site's pages hold at most.
    /// </summary>
    public IReadOnlyList<PageSizeChoice> PageSizes => _browse.PageSizes;

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

            PreviewLevel library = settings.PreviewFor(PreviewScreen.Library);
            HasOwnPreviews = own.Previews is not null;
            EveryGamesPreviews = Text.Format(Strings.GamePreviewsDefault, Describe(library), Describe(settings.PreviewFor(PreviewScreen.Browse)));
            Previews = own.Previews ?? library;

            BrowseDefaults browse = own.Browse ?? BrowseDefaults.None;
            HasOwnBrowse = own.Browse is not null;
            SelectedSort = browse.Sort is { } sort ? _browse.SortOf(sort, browse.Direction) ?? _browse.Default : _browse.Default;
            SelectedTimeWindow = _browse.SameTimeWindow(browse.UpdatedWithin);
            SelectedPageSize = _browse.SamePageSize(browse.PageSize);

            IsHidden = own.Hidden ?? false;
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>
    /// Says which other sites have a game named as this one, and offers to copy each one's settings.
    /// </summary>
    /// <param name="others">Each such game, with its name and site, such as "Stardew Valley (Example Mods)".</param>
    /// <param name="alsoOn">Such as "Also on Example Mods"; empty for none.</param>
    internal void ShowSameNamed(IReadOnlyList<KeyValuePair<GameRef, string>> others, string alsoOn)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(alsoOn);

        AlsoOnText = alsoOn;
        CopyChoices.SyncWith(
        [
            .. others.Select(other =>
            {
                string text = Text.Format(Strings.GameCopySettingsFrom, other.Value);
                return CopyChoices.FirstOrDefault(choice => choice.Game == other.Key && choice.Text == text)
                    ?? new GameCopyViewModel(other.Key, text, _copyFrom);
            }),
        ]);
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

    partial void OnPreviewsChanged(PreviewLevel value) => Save(own => own with { Previews = value });

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
    /// Gives the game previews of its own: the Library's, to change.
    /// </summary>
    [RelayCommand]
    private Task OverridePreviewsAsync() => ChangeAsync(own => own with { Previews = _shown.PreviewFor(PreviewScreen.Library) });

    [RelayCommand]
    private Task UseDefaultPreviewsAsync() => ChangeAsync(own => own with { Previews = null });

    /// <summary>
    /// Says how Browse starts for the game: as Browse's own does, to change.
    /// </summary>
    [RelayCommand]
    private Task OverrideBrowseAsync() => ChangeAsync(own => own with { Browse = BrowseOf(_browse.Default, TimeWindows[0], PageSizes[0]) });

    [RelayCommand]
    private Task UseDefaultBrowseAsync() => ChangeAsync(own => own with { Browse = null });

    /// <summary>
    /// Previews in a word or two, such as "thumbnails".
    /// </summary>
    private static string Describe(PreviewLevel level) => level switch
    {
        PreviewLevel.Off => Strings.GamePreviewsOffShort,
        PreviewLevel.Cards => Strings.GamePreviewsCardsShort,
        _ => Strings.GamePreviewsThumbnailsShort,
    };

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

    private void ChoosePreviews(bool chosen, PreviewLevel level)
    {
        if (chosen)
        {
            Previews = level;
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
