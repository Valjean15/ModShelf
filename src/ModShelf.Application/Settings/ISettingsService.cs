using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Settings;

/// <summary>
/// Reads and changes ModShelf's settings, applying their defaults and rules.
/// </summary>
/// <remarks>
/// Each change throws <see cref="InvalidDataException"/> when the stored settings can't be read, and
/// <see cref="IOException"/> when they can't be stored, naming where they're kept.
/// </remarks>
public interface ISettingsService
{
    /// <summary>
    /// Where the settings are kept, such as a file's path, to tell the user.
    /// </summary>
    string Location { get; }

    /// <summary>
    /// The library's root when none is set: a ModShelf folder in the user's Downloads folder.
    /// </summary>
    string DefaultLibraryRoot { get; }

    /// <returns>The settings as stored.</returns>
    /// <exception cref="InvalidDataException">The stored settings can't be read.</exception>
    Task<AppSettings> GetAsync(CancellationToken cancellationToken);

    /// <returns>The library's root folder: the one set, or the default.</returns>
    /// <exception cref="InvalidDataException">The stored settings can't be read.</exception>
    Task<string> GetLibraryRootAsync(CancellationToken cancellationToken);

    /// <returns>
    /// Where the library's files are: its root, the one set or the default, and the folders of games with their own.
    /// </returns>
    /// <exception cref="InvalidDataException">The stored settings can't be read.</exception>
    Task<Library.LibraryRoots> GetLibraryRootsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the library's root folder, or sets it back to the default with null. Only an empty library's root changes this
    /// way; <see cref="Library.ILibraryMover"/> moves a library with files. Games with folders of their own don't count.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="root"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The library has files under its current root, downloads into it aren't finished, or the new root is a game's
    /// folder, inside one, or holds one.
    /// </exception>
    Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken);

    /// <summary>
    /// Sets where downloaded files go: for every game, or for <paramref name="game"/> only. Null sets it back: a game to
    /// the pattern for every game, and that to <see cref="FolderPattern.Default"/>.
    /// </summary>
    Task SetLibraryFoldersAsync(GameRef? game, FolderPattern? pattern, CancellationToken cancellationToken);

    /// <summary>
    /// Changes <paramref name="game"/>'s own settings: <paramref name="change"/> takes those it has, or
    /// <see cref="GameSettings.None"/>, and gives those it should have. A game left with no setting of its own is
    /// forgotten.
    /// </summary>
    /// <remarks>
    /// Its folder (<see cref="GameSettings.Root"/>) changes this way only while it has no files;
    /// <see cref="Library.ILibraryMover.MoveGameAsync"/> moves a game with files.
    /// </remarks>
    /// <exception cref="ArgumentException">The game's new folder isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The game's folder would change while it has files, or downloads of its files into its folder aren't finished;
    /// or the new folder is the library's root or another game's folder, is inside one, or holds one.
    /// </exception>
    Task SetGameAsync(GameRef game, Func<GameSettings, GameSettings> change, CancellationToken cancellationToken);

    /// <summary>
    /// Gives <paramref name="game"/> the own settings <paramref name="other"/> has, in place of its own, such as the same
    /// game on another site: every one of them, and none when <paramref name="other"/> has none. The user chooses this;
    /// ModShelf never does it by itself. The game keeps its own folder (<see cref="GameSettings.Root"/>), which only
    /// moving its files changes.
    /// </summary>
    /// <exception cref="ArgumentException">Both are the same game.</exception>
    Task CopyGameAsync(GameRef other, GameRef game, CancellationToken cancellationToken);

    /// <summary>
    /// Sets what happens to a library file's old versions when a newer one is downloaded, or sets it back to
    /// <see cref="OldVersionRule.Default"/> with null. Files downloaded already stay as they are.
    /// </summary>
    Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken);

    /// <summary>
    /// Sets how often the watched mods are checked for updates in the background, or sets it back to
    /// <see cref="UpdateInterval.Default"/> with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The interval is shorter than <see cref="UpdateInterval.Minimum"/> or longer than
    /// <see cref="UpdateInterval.Maximum"/>.
    /// </exception>
    Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether a background check that finds new updates shows a notice, or sets it back to yes with null.
    /// </summary>
    Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether the desktop app is light or dark, or sets it back to <see cref="AppTheme.System"/> with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="theme"/> isn't an <see cref="AppTheme"/>.</exception>
    Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether closing the desktop app's window keeps ModShelf running in the tray or quits it, or sets it back to
    /// asking the next time with null.
    /// </summary>
    Task SetCloseToTrayAsync(bool? closeToTray, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether the desktop app's first-run setup is done, or sets it back to not yet with null, so that a new library
    /// shows it again at the next start.
    /// </summary>
    Task SetSetupDoneAsync(bool? done, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether the installed desktop app looks for a newer ModShelf when it starts, or sets it back to yes with null.
    /// </summary>
    Task SetCheckForAppUpdatesAsync(bool? check, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the previews <paramref name="screen"/> shows, or sets it back to its default with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="screen"/> isn't a <see cref="PreviewScreen"/>, or <paramref name="level"/> isn't a <see cref="PreviewLevel"/>.
    /// </exception>
    Task SetPreviewAsync(PreviewScreen screen, PreviewLevel? level, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the most room saved pictures take, in megabytes, or sets it back to
    /// <see cref="AppSettings.DefaultPicturesMegabytes"/> with null. The oldest go when it's full.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="megabytes"/> is below <see cref="AppSettings.MinPicturesMegabytes"/> or above <see cref="AppSettings.MaxPicturesMegabytes"/>.
    /// </exception>
    Task SetPicturesMegabytesAsync(int? megabytes, CancellationToken cancellationToken);

    /// <summary>
    /// Sets how long saved mod details count as current, or sets it back to <see cref="AppSettings.DefaultDetailsMaxAge"/>
    /// with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The age is shorter than <see cref="UpdateInterval.Minimum"/> or longer than <see cref="UpdateInterval.Maximum"/>.
    /// </exception>
    Task SetDetailsMaxAgeAsync(TimeSpan? age, CancellationToken cancellationToken);

    /// <summary>
    /// Sets how the pictures of mods with adult content show, or sets it back to <see cref="AdultPictures.Blurred"/> with
    /// null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pictures"/> isn't an <see cref="AdultPictures"/>.</exception>
    Task SetAdultPicturesAsync(AdultPictures? pictures, CancellationToken cancellationToken);

    /// <summary>
    /// Sets below what share of a site's allowance of requests the desktop app warns, in percent, 0 for never; or sets it
    /// back to <see cref="AppSettings.DefaultQuotaWarnBelow"/> with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="percent"/> is below 0 or above <see cref="AppSettings.MaxQuotaWarnBelow"/>.
    /// </exception>
    Task SetQuotaWarnBelowAsync(int? percent, CancellationToken cancellationToken);

    /// <summary>
    /// Sets whether AI agents may use ModShelf through its MCP server, or sets it back to no with null.
    /// </summary>
    Task SetAgentsEnabledAsync(bool? enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the port of the address the desktop app serves the MCP server at, or sets it back to
    /// <see cref="AppSettings.DefaultAgentsPort"/> with null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is below <see cref="AppSettings.MinAgentsPort"/> or above <see cref="AppSettings.MaxAgentsPort"/>.
    /// </exception>
    Task SetAgentsPortAsync(int? port, CancellationToken cancellationToken);
}
