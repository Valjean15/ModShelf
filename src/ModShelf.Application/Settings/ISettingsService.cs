using ModShelf.Domain.Library;

namespace ModShelf.Application.Settings;

/// <summary>
/// Reads and changes ModShelf's settings, applying their defaults and rules.
/// </summary>
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
    /// Sets the library's root folder, or sets it back to the default with null.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="root"/> isn't an absolute path.</exception>
    /// <exception cref="InvalidOperationException">
    /// The library has files under its current root. Until ModShelf can move them, the root only changes while it has none.
    /// </exception>
    Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken);

    /// <summary>
    /// Sets where downloaded files go: for every game, or for <paramref name="game"/> only. Null sets it back: a game to
    /// the pattern for every game, and that to <see cref="FolderPattern.Default"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="game"/> is blank.</exception>
    Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken);
}
