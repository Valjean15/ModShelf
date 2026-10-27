using System.Collections.Immutable;

using ModShelf.Domain.Library;

namespace ModShelf.Application.Settings;

/// <summary>
/// ModShelf's settings, as stored. A setting that isn't set takes its default.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// The library's root folder, as an absolute path; null for the default, a ModShelf folder in the user's Downloads
    /// folder.
    /// </summary>
    public string? LibraryRoot { get; init; }

    /// <summary>
    /// Where downloaded files go in the library, for every game without a pattern of its own; null for
    /// <see cref="FolderPattern.Default"/>.
    /// </summary>
    public FolderPattern? LibraryFolders { get; init; }

    /// <summary>
    /// Patterns of single games, by the game as its site names it, such as skyrimspecialedition, in any case.
    /// </summary>
    public IReadOnlyDictionary<string, FolderPattern> GameFolders { get; init; }
        = ImmutableDictionary.Create<string, FolderPattern>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What happens to a library file's old versions when a newer one is downloaded; null for
    /// <see cref="OldVersionRule.Default"/>.
    /// </summary>
    public OldVersionRule? OldVersions { get; init; }

    /// <summary>
    /// How often the watched mods are checked for updates in the background; null for <see cref="Domain.Updates.UpdateInterval.Default"/>.
    /// </summary>
    public TimeSpan? UpdateInterval { get; init; }

    /// <summary>
    /// The pattern files of <paramref name="game"/> go by.
    /// </summary>
    public FolderPattern FoldersFor(string game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return GameFolders.TryGetValue(game, out FolderPattern? pattern) ? pattern : LibraryFolders ?? FolderPattern.Default;
    }
}
