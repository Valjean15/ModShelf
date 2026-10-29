using ModShelf.Domain.Library;
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

    /// <summary>
    /// Sets the library's root folder, or sets it back to the default with null. Only an empty library's root changes this
    /// way; <see cref="Library.ILibraryMover"/> moves a library with files.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="root"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The library has files under its current root, or downloads into it aren't finished.
    /// </exception>
    Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken);

    /// <summary>
    /// Sets where downloaded files go: for every game, or for <paramref name="game"/> only. Null sets it back: a game to
    /// the pattern for every game, and that to <see cref="FolderPattern.Default"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="game"/> is blank.</exception>
    Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken);

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
}
