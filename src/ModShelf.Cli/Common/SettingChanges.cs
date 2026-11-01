using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

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
    public const string Names = $"{LibraryRoot}, {LibraryFolders}, {GamePrefix}<game>, {OldVersions}, {Interval}, {Notify}, {Theme}, {CloseToTray}, {SetupDone}, {CheckForAppUpdates}";

    /// <summary>
    /// What each setting's value is.
    /// </summary>
    public const string Values =
        "a full path for library.root; all, archive or how many versions to keep for "
        + "library.oldVersions; minutes, hours or days, such as 30m, 6h or 1d, for updates.interval; true or false for "
        + "updates.notify, window.closeToTray, setup.done and app.checkForUpdates; system, light or dark for appearance.theme; a folder pattern, "
        + "such as \"{source}/{game}/{mod} ({modid})/{file}\", for the others";

    /// <summary>
    /// Whether <paramref name="name"/> is a setting's, in any case, as a game's folder pattern is by its game.
    /// </summary>
    public static bool IsSetting(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string setting = name.Trim().ToLowerInvariant();
        return setting is LibraryRoot or LibraryFolders or Interval or Notify or Theme or SetupDone
            || setting.Equals(CloseToTray, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(CheckForAppUpdates, StringComparison.OrdinalIgnoreCase)
            || setting.Equals(OldVersions, StringComparison.OrdinalIgnoreCase)
            || (setting.StartsWith(GamePrefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(name.Trim()[GamePrefix.Length..]));
    }

    public static bool IsLibraryRoot(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().Equals(LibraryRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every setting, the ones left at their defaults too, with the default's value; a game's folder pattern only when
    /// it's set.
    /// </summary>
    /// <param name="defaultLibraryRoot">Where the library is when <see cref="LibraryRoot"/> isn't set.</param>
    public static IReadOnlyList<SettingValue> List(AppSettings stored, string defaultLibraryRoot)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return
        [
            Of(LibraryRoot, stored.LibraryRoot, defaultLibraryRoot),
            Of(LibraryFolders, stored.LibraryFolders?.Text, FolderPattern.Default.Text),
            .. stored.GameFolders
                .OrderBy(game => game.Key, StringComparer.OrdinalIgnoreCase)
                .Select(game => new SettingValue(GamePrefix + game.Key, game.Value.Text, IsDefault: false)),
            Of(OldVersions, stored.OldVersions?.ToString(), OldVersionRule.Default.ToString()),
            Of(Interval, stored.UpdateInterval is { } every ? UpdateInterval.Format(every) : null, UpdateInterval.Format(UpdateInterval.Default)),
            Of(Notify, stored.NotifyOfUpdates is { } notify ? Text(notify) : null, Text(true)),
            Of(Theme, stored.Theme is { } theme ? ThemeName(theme) : null, ThemeName(AppTheme.System)),
            Of(CloseToTray, stored.CloseToTray is { } closeToTray ? Text(closeToTray) : null, "ask"),
            Of(SetupDone, stored.SetupDone is { } done ? Text(done) : null, Text(false)),
            Of(CheckForAppUpdates, stored.CheckForAppUpdates is { } check ? Text(check) : null, Text(true)),
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

    /// <exception cref="ArgumentException"><paramref name="value"/> is neither true nor false.</exception>
    private static bool ParseBool(string value)
        => bool.TryParse(value.Trim(), out bool parsed) ? parsed : throw new ArgumentException($"'{value}' isn't true or false.", nameof(value));
}
