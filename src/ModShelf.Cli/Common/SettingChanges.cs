using System.Globalization;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Common;

/// <summary>
/// The settings in <c>settings.json</c>, by the names the CLI and its MCP server give them: what each is, and changing
/// one from text, with what it is afterwards in a sentence.
/// </summary>
internal static class SettingChanges
{
    public const string LibraryRoot = "library.root";
    public const string LibraryFolders = "library.folders";
    public const string GamePrefix = LibraryFolders + ".";
    public const string OldVersions = "library.oldVersions";
    public const string Interval = "updates.interval";
    public const string Notify = "updates.notify";
    public const string Theme = "appearance.theme";
    public const string CloseToTray = "window.closeToTray";
    public const string SetupDone = "setup.done";
    public const string CheckForAppUpdates = "app.checkForUpdates";
    public const string PreviewsPrefix = "previews.";
    public const string PicturesMaxSize = "pictures.maxSize";
    public const string AdultPictures = "pictures.adult";
    public const string DetailsMaxAge = "details.maxAge";
    public const string GamesPrefix = "games.";
    public const string Names = $"{LibraryRoot}, {LibraryFolders}, {GamePrefix}<game>, {OldVersions}, {Interval}, {Notify}, {Theme}, {CloseToTray}, {SetupDone}, {CheckForAppUpdates}, {PreviewsPrefix}library, {PreviewsPrefix}updates, {PreviewsPrefix}browse, {PreviewsPrefix}downloads, {PicturesMaxSize}, {AdultPictures}, {DetailsMaxAge}, "
        + $"{GamesPrefix}<game>.{GameFolders}, {GamesPrefix}<game>.{GameOldVersions}, {GamesPrefix}<game>.{GameCheckUpdates}, {GamesPrefix}<game>.{GameInterval}, {GamesPrefix}<game>.{GameNotify}, {GamesPrefix}<game>.{GameHidden}, "
        + $"{GamesPrefix}<game>.{BrowseSort}, {GamesPrefix}<game>.{BrowseDirection}, {GamesPrefix}<game>.{BrowseWindow}, {GamesPrefix}<game>.{BrowsePageSize}";

    private const string GameFolders = "folders";
    private const string GameOldVersions = "oldVersions";
    private const string GameCheckUpdates = "checkUpdates";
    private const string GameInterval = "updateInterval";
    private const string GameNotify = "notify";
    private const string GameHidden = "hidden";
    private const string BrowseSort = "browse.sort";
    private const string BrowseDirection = "browse.direction";
    private const string BrowseWindow = "browse.window";
    private const string BrowsePageSize = "browse.pageSize";

    /// <summary>
    /// The settings a game can have of its own, by the names that follow <c>games.&lt;game&gt;.</c>.
    /// </summary>
    private static readonly string[] GameSettingNames =
        [GameFolders, GameOldVersions, GameCheckUpdates, GameInterval, GameNotify, GameHidden, BrowseSort, BrowseDirection, BrowseWindow, BrowsePageSize];

    /// <summary>
    /// What each setting's value is.
    /// </summary>
    public const string Values =
        "a full path for library.root; all, archive or how many versions to keep for "
        + "library.oldVersions; minutes, hours or days, such as 30m, 6h or 1d, for updates.interval and details.maxAge; true or false for "
        + "updates.notify, window.closeToTray, setup.done and app.checkForUpdates; system, light or dark for appearance.theme; "
        + "off, thumbnails or cards for previews.<screen>; a number of megabytes from 64 to 8192 for pictures.maxSize; "
        + "hidden, blurred or shown for pictures.adult; for a game of its own, as every game's for games.<game>.oldVersions, "
        + "games.<game>.updateInterval and games.<game>.notify, true or false for games.<game>.checkUpdates and games.<game>.hidden, "
        + "relevance, endorsements, downloads, updated, newest, name, size or uniquedownloads for games.<game>.browse.sort, ascending or descending "
        + "for games.<game>.browse.direction, 1d, 7d or 30d for games.<game>.browse.window, and 20, 50 or 100 for games.<game>.browse.pageSize; "
        + "a folder pattern, such as \"{source}/{game}/{mod} ({modid})/{file}\", for the others";

    /// <summary>
    /// Whether <paramref name="name"/> is a setting's, in any case, as a game's folder pattern is by its game.
    /// </summary>
    public static bool IsSetting(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string setting = name.Trim().ToLowerInvariant();
        return setting is LibraryRoot or LibraryFolders or Interval or Notify or Theme or SetupDone
            || PreviewScreenOf(setting) is not null
            || setting.Equals(PicturesMaxSize, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(AdultPictures, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(DetailsMaxAge, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(CloseToTray, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(CheckForAppUpdates, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(OldVersions, StringComparison.OrdinalIgnoreCase)
            || (setting.StartsWith(GamePrefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(name.Trim()[GamePrefix.Length..]))
            || GameSettingOf(name) is not null;
    }

    public static bool IsLibraryRoot(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().Equals(LibraryRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every setting, the ones left at their defaults too, with the default's value; a game's own settings only when
    /// they're set, its folder pattern by its older name, <c>library.folders.&lt;game&gt;</c>.
    /// </summary>
    /// <param name="defaultLibraryRoot">Where the library is when <see cref="LibraryRoot"/> isn't set.</param>
    public static IReadOnlyList<SettingValue> List(AppSettings stored, string defaultLibraryRoot)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return
        [
            Of(LibraryRoot, stored.LibraryRoot, defaultLibraryRoot),
            Of(LibraryFolders, stored.LibraryFolders?.Text, FolderPattern.Default.Text),
            .. stored.Games
                .Where(game => game.Value.Folders is not null)
                .OrderBy(game => game.Key, StringComparer.OrdinalIgnoreCase)
                .Select(game => new SettingValue(GamePrefix + game.Key, game.Value.Folders!.Text, IsDefault: false)),
            Of(OldVersions, stored.OldVersions?.ToString(), OldVersionRule.Default.ToString()),
            Of(Interval, stored.UpdateInterval is { } every ? UpdateInterval.Format(every) : null, UpdateInterval.Format(UpdateInterval.Default)),
            Of(Notify, stored.NotifyOfUpdates is { } notify ? Text(notify) : null, Text(true)),
            Of(Theme, stored.Theme is { } theme ? ThemeName(theme) : null, ThemeName(AppTheme.System)),
            Of(CloseToTray, stored.CloseToTray is { } closeToTray ? Text(closeToTray) : null, "ask"),
            Of(SetupDone, stored.SetupDone is { } done ? Text(done) : null, Text(false)),
            Of(CheckForAppUpdates, stored.CheckForAppUpdates is { } check ? Text(check) : null, Text(true)),
            .. Enum.GetValues<PreviewScreen>().Select(screen => Of(
                PreviewsPrefix + Name(screen),
                stored.Previews.TryGetValue(screen, out PreviewLevel level) ? Name(level) : null,
                Name(AppSettings.DefaultPreview(screen)))),
            Of(PicturesMaxSize, stored.PicturesMegabytes?.ToString(CultureInfo.InvariantCulture), AppSettings.DefaultPicturesMegabytes.ToString(CultureInfo.InvariantCulture)),
            Of(AdultPictures, stored.AdultPictures is { } adult ? Name(adult) : null, Name(Application.Settings.AdultPictures.Blurred)),
            Of(DetailsMaxAge, stored.DetailsMaxAge is { } age ? UpdateInterval.Format(age) : null, UpdateInterval.Format(AppSettings.DefaultDetailsMaxAge)),
            .. stored.Games
                .OrderBy(game => game.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(game => GameValues(game.Key, game.Value)),
        ];
    }

    /// <summary>
    /// Changes a setting.
    /// </summary>
    /// <param name="value">The setting's new value, or null to set it back to its default.</param>
    /// <returns>What the setting is now, as a sentence.</returns>
    /// <exception cref="ArgumentException">There's no such setting, or <paramref name="value"/> isn't one of its values.</exception>
    /// <exception cref="InvalidOperationException">
    /// The library's root can't change now: its library has files, which moving the library moves, or downloads under way.
    /// </exception>
    public static async Task<string> ChangeAsync(ISettingsService settings, string name, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(name);
        if (!IsSetting(name))
        {
            throw new ArgumentException($"There's no setting called {name}. Settings: {Names}.", nameof(name));
        }

        if (GameSettingOf(name) is { } own)
        {
            return await ChangeGameAsync(settings, name.Trim(), own.Game, own.Setting, value, cancellationToken).ConfigureAwait(false);
        }

        string setting = name.Trim().ToLowerInvariant();
        string? game = setting.StartsWith(GamePrefix, StringComparison.Ordinal) ? name.Trim()[GamePrefix.Length..] : null;
        if (setting == LibraryRoot)
        {
            await settings.SetLibraryRootAsync(value, cancellationToken).ConfigureAwait(false);
            string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            return value is null ? $"{LibraryRoot} is back to its default, {root}." : $"{LibraryRoot} is now {root}.";
        }

        if (setting == Interval)
        {
            TimeSpan? interval = value is null ? null : UpdateInterval.Parse(value);
            await settings.SetUpdateIntervalAsync(interval, cancellationToken).ConfigureAwait(false);
            return interval is { } every
                ? $"{Interval} is now {UpdateInterval.Format(every)}."
                : $"{Interval} is back to its default, {UpdateInterval.Format(UpdateInterval.Default)}.";
        }

        if (setting == Notify)
        {
            bool? notify = value is null ? null : ParseBool(value);
            await settings.SetNotifyOfUpdatesAsync(notify, cancellationToken).ConfigureAwait(false);
            string what = notify ?? true
                ? "the desktop app shows a notice when a background check finds new updates."
                : "the desktop app shows no notice when a background check finds new updates.";
            return notify is { } now ? $"{Notify} is now {Text(now)}: {what}" : $"{Notify} is back to its default, {Text(true)}: {what}";
        }

        if (setting.Equals(CloseToTray, StringComparison.OrdinalIgnoreCase))
        {
            bool? keepRunning = value is null ? null : ParseBool(value);
            await settings.SetCloseToTrayAsync(keepRunning, cancellationToken).ConfigureAwait(false);
            return keepRunning switch
            {
                true => $"{CloseToTray} is now true: closing the desktop app's window keeps ModShelf running in the tray.",
                false => $"{CloseToTray} is now false: closing the desktop app's window quits ModShelf.",
                null => $"{CloseToTray} is back to its default: the desktop app asks the first time its window closes.",
            };
        }

        if (PreviewScreenOf(setting) is { } screen)
        {
            PreviewLevel? level = value is null ? null : Parse<PreviewLevel>(value, "off, thumbnails or cards");
            await settings.SetPreviewAsync(screen, level, cancellationToken).ConfigureAwait(false);
            PreviewLevel now = level ?? AppSettings.DefaultPreview(screen);
            string previews = PreviewsPrefix + Name(screen);
            return level is null
                ? $"{previews} is back to its default, {Name(now)}: {Describe(screen, now)}"
                : $"{previews} is now {Name(now)}: {Describe(screen, now)}";
        }

        if (setting.Equals(PicturesMaxSize, StringComparison.OrdinalIgnoreCase))
        {
            int? megabytes = value is null ? null : ParseMegabytes(value);
            await settings.SetPicturesMegabytesAsync(megabytes, cancellationToken).ConfigureAwait(false);
            int now = megabytes ?? AppSettings.DefaultPicturesMegabytes;
            string what = $"saved pictures take at most {now.ToString(CultureInfo.InvariantCulture)} MB, the oldest going first.";
            return megabytes is null ? $"{PicturesMaxSize} is back to its default, {now.ToString(CultureInfo.InvariantCulture)}: {what}" : $"{PicturesMaxSize} is now {now.ToString(CultureInfo.InvariantCulture)}: {what}";
        }

        if (setting.Equals(AdultPictures, StringComparison.OrdinalIgnoreCase))
        {
            AdultPictures? pictures = value is null ? null : Parse<AdultPictures>(value, "hidden, blurred or shown");
            await settings.SetAdultPicturesAsync(pictures, cancellationToken).ConfigureAwait(false);
            AdultPictures now = pictures ?? Application.Settings.AdultPictures.Blurred;
            return pictures is null
                ? $"{AdultPictures} is back to its default, {Name(now)}: {Describe(now)}"
                : $"{AdultPictures} is now {Name(now)}: {Describe(now)}";
        }

        if (setting.Equals(DetailsMaxAge, StringComparison.OrdinalIgnoreCase))
        {
            TimeSpan? age = value is null ? null : UpdateInterval.Parse(value);
            await settings.SetDetailsMaxAgeAsync(age, cancellationToken).ConfigureAwait(false);
            string now = UpdateInterval.Format(age ?? AppSettings.DefaultDetailsMaxAge);
            string what = $"saved mod details are read from their site again once they're {now} old.";
            return age is null ? $"{DetailsMaxAge} is back to its default, {now}: {what}" : $"{DetailsMaxAge} is now {now}: {what}";
        }

        if (setting.Equals(CheckForAppUpdates, StringComparison.OrdinalIgnoreCase))
        {
            bool? check = value is null ? null : ParseBool(value);
            await settings.SetCheckForAppUpdatesAsync(check, cancellationToken).ConfigureAwait(false);
            return check ?? true
                ? $"{CheckForAppUpdates} is {(check is null ? "back to its default, true" : "now true")}: the installed desktop app looks for a newer ModShelf when it starts."
                : $"{CheckForAppUpdates} is now false: the installed desktop app looks for a newer ModShelf only when asked, on its Settings screen.";
        }

        if (setting == SetupDone)
        {
            bool? done = value is null ? null : ParseBool(value);
            await settings.SetSetupDoneAsync(done, cancellationToken).ConfigureAwait(false);
            return done ?? false
                ? $"{SetupDone} is now true: the desktop app doesn't show its first-run setup."
                : $"{SetupDone} is {(done is null ? "back to its default, false" : "now false")}: a new library shows the desktop app's first-run setup at its next start.";
        }

        if (setting == Theme)
        {
            AppTheme? theme = value is null ? null : ParseTheme(value);
            await settings.SetThemeAsync(theme, cancellationToken).ConfigureAwait(false);
            return theme is { } now
                ? $"{Theme} is now {ThemeName(now)}: {Describe(now)}"
                : $"{Theme} is back to its default, {ThemeName(AppTheme.System)}: {Describe(AppTheme.System)}";
        }

        if (setting.Equals(OldVersions, StringComparison.OrdinalIgnoreCase))
        {
            OldVersionRule? rule = value is null ? null : OldVersionRule.Parse(value);
            await settings.SetOldVersionsAsync(rule, cancellationToken).ConfigureAwait(false);
            return rule is null
                ? $"{OldVersions} is back to its default, {OldVersionRule.Default}: {Describe(OldVersionRule.Default)}"
                : $"{OldVersions} is now {rule}: {Describe(rule)}";
        }

        FolderPattern? pattern = value is null ? null : FolderPattern.Parse(value);
        await settings.SetLibraryFoldersAsync(game, pattern, cancellationToken).ConfigureAwait(false);
        string named = game is null ? LibraryFolders : GamePrefix + game;
        return (pattern, game) switch
        {
            (not null, _) => $"{named} is now {pattern.Text}.",
            (null, null) => $"{LibraryFolders} is back to its default, {FolderPattern.Default.Text}.",
            (null, _) => $"{named} is gone, so {game}'s files go by {LibraryFolders} again.",
        };
    }

    private static SettingValue Of(string name, string? value, string defaultValue)
        => new(name, value ?? defaultValue, IsDefault: value is null);

    /// <summary>
    /// The game and the name of its setting in <c>games.&lt;game&gt;.&lt;setting&gt;</c>, in any case; null for another
    /// setting.
    /// </summary>
    private static (string Game, string Setting)? GameSettingOf(string name)
    {
        string trimmed = name.Trim();
        if (!trimmed.StartsWith(GamesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = trimmed[GamesPrefix.Length..];
        foreach (string setting in GameSettingNames)
        {
            if (rest.EndsWith("." + setting, StringComparison.OrdinalIgnoreCase)
                && rest[..^(setting.Length + 1)] is { } game
                && !string.IsNullOrWhiteSpace(game)
                && !game.Contains('.', StringComparison.Ordinal))
            {
                return (game.Trim(), setting);
            }
        }

        return null;
    }

    /// <summary>
    /// A game's own settings but its folder pattern, as <c>games.&lt;game&gt;.&lt;setting&gt;</c>.
    /// </summary>
    private static IEnumerable<SettingValue> GameValues(string game, GameSettings own)
    {
        string prefix = $"{GamesPrefix}{game}.";
        (string Name, string? Value)[] values =
        [
            (GameOldVersions, own.OldVersions?.ToString()),
            (GameCheckUpdates, own.CheckUpdates is { } check ? Text(check) : null),
            (GameInterval, own.UpdateInterval is { } interval ? UpdateInterval.Format(interval) : null),
            (GameNotify, own.Notify is { } notify ? Text(notify) : null),
            (GameHidden, own.Hidden is { } hidden ? Text(hidden) : null),
            (BrowseSort, own.Browse?.Sort is { } sort ? Name(sort) : null),
            (BrowseDirection, own.Browse?.Direction is { } direction ? Name(direction) : null),
            (BrowseWindow, own.Browse?.UpdatedWithin is { } window ? WindowText(window) : null),
            (BrowsePageSize, own.Browse?.PageSize?.ToString(CultureInfo.InvariantCulture)),
        ];
        return values.Where(value => value.Value is not null).Select(value => new SettingValue(prefix + value.Name, value.Value!, IsDefault: false));
    }

    /// <summary>
    /// Names as a choice, such as "a, b or c".
    /// </summary>
    private static string OneOf(IEnumerable<string> names)
    {
        string[] all = [.. names];
        return all.Length < 2 ? string.Concat(all) : $"{string.Join(", ", all[..^1])} or {all[^1]}";
    }

    private static string WindowText(TimeSpan window) => $"{window.TotalDays.ToString(CultureInfo.InvariantCulture)}d";

    /// <summary>
    /// Changes one of <paramref name="game"/>'s own settings, <paramref name="setting"/>; null sets it back to every
    /// game's.
    /// </summary>
    private static async Task<string> ChangeGameAsync(
        ISettingsService settings,
        string named,
        string game,
        string setting,
        string? value,
        CancellationToken cancellationToken)
    {
        if (setting == GameFolders)
        {
            FolderPattern? pattern = value is null ? null : FolderPattern.Parse(value);
            await settings.SetLibraryFoldersAsync(game, pattern, cancellationToken).ConfigureAwait(false);
            return pattern is null ? $"{named} is gone, so {game}'s files go by {LibraryFolders} again." : $"{named} is now {pattern.Text}.";
        }

        if (setting == GameOldVersions)
        {
            OldVersionRule? rule = value is null ? null : OldVersionRule.Parse(value);
            await settings.SetGameAsync(game, own => own with { OldVersions = rule }, cancellationToken).ConfigureAwait(false);
            return rule is null ? $"{named} is gone, so {game} goes by {OldVersions} again." : $"{named} is now {rule}: {Describe(rule)}";
        }

        if (setting == GameInterval)
        {
            TimeSpan? interval = value is null ? null : UpdateInterval.Parse(value);
            await settings.SetGameAsync(game, own => own with { UpdateInterval = interval }, cancellationToken).ConfigureAwait(false);
            return interval is { } every
                ? $"{named} is now {UpdateInterval.Format(every)}: the desktop app checks {game}'s watched mods for updates that often."
                : $"{named} is gone, so {game} goes by {Interval} again.";
        }

        if (setting is BrowseSort or BrowseDirection or BrowseWindow or BrowsePageSize)
        {
            return await ChangeBrowseAsync(settings, named, game, setting, value, cancellationToken).ConfigureAwait(false);
        }

        bool? flag = value is null ? null : ParseBool(value);
        await settings.SetGameAsync(
                game,
                own => setting switch
                {
                    GameCheckUpdates => own with { CheckUpdates = flag },
                    GameNotify => own with { Notify = flag },
                    _ => own with { Hidden = flag },
                },
                cancellationToken)
            .ConfigureAwait(false);
        return (setting, flag) switch
        {
            (GameCheckUpdates, true) => $"{named} is now true: {game}'s watched mods are checked for updates with every game's.",
            (GameCheckUpdates, false) => $"{named} is now false: {game}'s watched mods are checked for updates only when the game alone is checked.",
            (GameCheckUpdates, null) => $"{named} is gone, so {game}'s watched mods are checked for updates with every game's.",
            (GameNotify, true) => $"{named} is now true: the desktop app shows a notice when a background check finds new updates of {game}'s mods.",
            (GameNotify, false) => $"{named} is now false: the desktop app shows no notice when a background check finds new updates of {game}'s mods.",
            (GameNotify, null) => $"{named} is gone, so {game} goes by {Notify} again.",
            (_, true) => $"{named} is now true: the desktop app's lists leave {game} out, and its mods are still checked for updates.",
            (_, false) => $"{named} is now false: the desktop app's lists show {game}.",
            _ => $"{named} is gone, so the desktop app's lists show {game}.",
        };
    }

    /// <summary>
    /// Changes how Browse shows <paramref name="game"/>'s mods; null sets it back to Browse's own.
    /// </summary>
    private static async Task<string> ChangeBrowseAsync(
        ISettingsService settings,
        string named,
        string game,
        string setting,
        string? value,
        CancellationToken cancellationToken)
    {
        string? text = null;
        Func<BrowseDefaults, BrowseDefaults> change;
        switch (setting)
        {
            case BrowseSort:
                SearchSort? sort = value is null ? null : Parse<SearchSort>(value, OneOf(Enum.GetValues<SearchSort>().Select(Name)));
                text = sort is { } sorted ? Name(sorted) : null;
                change = browse => browse with { Sort = sort };
                break;
            case BrowseDirection:
                SearchDirection? direction = value is null ? null : Parse<SearchDirection>(value, "ascending or descending");
                text = direction is { } going ? Name(going) : null;
                change = browse => browse with { Direction = direction };
                break;
            case BrowseWindow:
                TimeSpan? window = value is null
                    ? null
                    : BrowseDefaults.Windows.Cast<TimeSpan?>().FirstOrDefault(each => WindowText(each!.Value).Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"'{value}' isn't 1d, 7d or 30d.", nameof(value));
                text = window is { } within ? WindowText(within) : null;
                change = browse => browse with { UpdatedWithin = window };
                break;
            default:
                int? size = value is null
                    ? null
                    : int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && BrowseDefaults.PageSizes.Contains(parsed)
                        ? parsed
                        : throw new ArgumentException($"'{value}' isn't 20, 50 or 100.", nameof(value));
                text = size?.ToString(CultureInfo.InvariantCulture);
                change = browse => browse with { PageSize = size };
                break;
        }

        await settings.SetGameAsync(game, own => own with { Browse = change(own.Browse ?? BrowseDefaults.None) }, cancellationToken).ConfigureAwait(false);
        return text is null
            ? $"{named} is gone, so the desktop app's Browse screen shows {game}'s mods as it would any game's."
            : $"{named} is now {text}: the desktop app's Browse screen starts so when {game} is chosen.";
    }

    private static string Text(bool value) => value ? "true" : "false";

    private static string ThemeName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system",
    };

    /// <summary>
    /// What a theme does, as a sentence.
    /// </summary>
    private static string Describe(AppTheme theme) => theme switch
    {
        AppTheme.Light => "the desktop app is light.",
        AppTheme.Dark => "the desktop app is dark.",
        _ => "the desktop app is light or dark as the system is.",
    };

    /// <exception cref="ArgumentException"><paramref name="value"/> names no theme.</exception>
    private static AppTheme ParseTheme(string value)
        => Enum.GetValues<AppTheme>().Cast<AppTheme?>().FirstOrDefault(theme => ThemeName(theme!.Value).Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{value}' isn't system, light or dark.", nameof(value));

    /// <summary>
    /// What a rule does with old versions, as a sentence.
    /// </summary>
    private static string Describe(OldVersionRule rule) => rule switch
    {
        { Action: OldVersionAction.KeepAll } => "every version of a file stays.",
        { Action: OldVersionAction.Archive } => "when a newer version of a file is downloaded, the old ones move to an _archive folder.",
        { Count: 1 } => "when a newer version of a file is downloaded, the old ones are deleted.",
        _ => $"when a newer version of a file is downloaded, the newest {rule.Count} stay and older ones are deleted.",
    };

    /// <summary>
    /// The screen a previews setting is of, such as the Library's for previews.library; null when it's another setting.
    /// </summary>
    private static PreviewScreen? PreviewScreenOf(string setting)
        => setting.StartsWith(PreviewsPrefix, StringComparison.OrdinalIgnoreCase)
            ? Enum.GetValues<PreviewScreen>().Cast<PreviewScreen?>().FirstOrDefault(screen => Name(screen!.Value).Equals(setting[PreviewsPrefix.Length..], StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// A value as the settings name it: in lower case, such as thumbnails.
    /// </summary>
    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    /// <exception cref="ArgumentException"><paramref name="value"/> names none of <typeparamref name="T"/>'s values.</exception>
    private static T Parse<T>(string value, string names) where T : struct, Enum
        => Enum.GetValues<T>().Cast<T?>().FirstOrDefault(each => Name(each!.Value).Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{value}' isn't {names}.", nameof(value));

    /// <exception cref="ArgumentException"><paramref name="value"/> isn't a whole number of megabytes within the limits.</exception>
    private static int ParseMegabytes(string value)
        => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int megabytes)
            && megabytes is >= AppSettings.MinPicturesMegabytes and <= AppSettings.MaxPicturesMegabytes
            ? megabytes
            : throw new ArgumentException(
                $"'{value}' isn't a number of megabytes from {AppSettings.MinPicturesMegabytes} to {AppSettings.MaxPicturesMegabytes}.",
                nameof(value));

    /// <summary>
    /// What a screen shows at a level, as a sentence.
    /// </summary>
    private static string Describe(PreviewScreen screen, PreviewLevel level) => level switch
    {
        PreviewLevel.Thumbnails => $"the desktop app's {screen} screen shows a small picture at the start of each row.",
        PreviewLevel.Cards => $"the desktop app's {screen} screen shows its mods as cards, with large pictures.",
        _ => $"the desktop app's {screen} screen shows no pictures.",
    };

    /// <summary>
    /// How adult mods' pictures show, as a sentence.
    /// </summary>
    private static string Describe(AdultPictures pictures) => pictures switch
    {
        Application.Settings.AdultPictures.Hidden => "the desktop app shows no picture for mods with adult content.",
        Application.Settings.AdultPictures.Shown => "the desktop app shows the pictures of mods with adult content as they are.",
        _ => "the desktop app shows the pictures of mods with adult content blurred.",
    };

    /// <exception cref="ArgumentException"><paramref name="value"/> is neither true nor false.</exception>
    private static bool ParseBool(string value)
        => bool.TryParse(value.Trim(), out bool parsed) ? parsed : throw new ArgumentException($"'{value}' isn't true or false.", nameof(value));
}
