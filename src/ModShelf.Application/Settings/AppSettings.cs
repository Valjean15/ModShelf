using System.Collections.Immutable;

using ModShelf.Domain.Library;

namespace ModShelf.Application.Settings;

/// <summary>
/// ModShelf's settings, as stored. A setting that isn't set takes its default.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// The most room saved pictures take when <see cref="PicturesMegabytes"/> isn't set.
    /// </summary>
    public const int DefaultPicturesMegabytes = 512;

    /// <summary>
    /// The least room saved pictures can be given, in megabytes.
    /// </summary>
    public const int MinPicturesMegabytes = 64;

    /// <summary>
    /// The most room saved pictures can be given, in megabytes.
    /// </summary>
    public const int MaxPicturesMegabytes = 8192;

    /// <summary>
    /// How long saved mod details count as current when <see cref="DetailsMaxAge"/> isn't set.
    /// </summary>
    public static TimeSpan DefaultDetailsMaxAge { get; } = TimeSpan.FromDays(1);

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
    /// Whether a background check that finds new updates shows a notice; null for yes.
    /// </summary>
    public bool? NotifyOfUpdates { get; init; }

    /// <summary>
    /// Whether the desktop app is light or dark; null for <see cref="AppTheme.System"/>.
    /// </summary>
    public AppTheme? Theme { get; init; }

    /// <summary>
    /// Whether closing the desktop app's window keeps ModShelf running in the tray (true) or quits it (false); null to
    /// ask the first time the window closes.
    /// </summary>
    public bool? CloseToTray { get; init; }

    /// <summary>
    /// Whether the desktop app's first-run setup is done, finished or skipped; null for not yet, which shows it at the
    /// next start of a new library.
    /// </summary>
    public bool? SetupDone { get; init; }

    /// <summary>
    /// Whether the installed desktop app looks for a newer ModShelf when it starts; null for yes.
    /// </summary>
    public bool? CheckForAppUpdates { get; init; }

    /// <summary>
    /// The previews each screen shows, by screen; a screen left out shows its default (<see cref="PreviewFor"/>).
    /// </summary>
    public IReadOnlyDictionary<PreviewScreen, PreviewLevel> Previews { get; init; }
        = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty;

    /// <summary>
    /// The most room saved pictures take, in megabytes, before the oldest are dropped; null for
    /// <see cref="DefaultPicturesMegabytes"/>.
    /// </summary>
    public int? PicturesMegabytes { get; init; }

    /// <summary>
    /// How long saved mod details count as current before they're read from the site again; null for
    /// <see cref="DefaultDetailsMaxAge"/>.
    /// </summary>
    public TimeSpan? DetailsMaxAge { get; init; }

    /// <summary>
    /// How the pictures of mods with adult content show; null for <see cref="Settings.AdultPictures.Blurred"/>.
    /// </summary>
    public AdultPictures? AdultPictures { get; init; }

    /// <summary>
    /// The previews <paramref name="screen"/> shows: as set, or its default, thumbnails on the Library screen, cards on
    /// Browse, and none on the others.
    /// </summary>
    public PreviewLevel PreviewFor(PreviewScreen screen)
        => Previews.TryGetValue(screen, out PreviewLevel level) ? level : DefaultPreview(screen);

    /// <summary>
    /// The previews <paramref name="screen"/> shows when it's not set.
    /// </summary>
    public static PreviewLevel DefaultPreview(PreviewScreen screen) => screen switch
    {
        PreviewScreen.Library => PreviewLevel.Thumbnails,
        PreviewScreen.Browse => PreviewLevel.Cards,
        _ => PreviewLevel.Off,
    };

    /// <summary>
    /// The pattern files of <paramref name="game"/> go by.
    /// </summary>
    public FolderPattern FoldersFor(string game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return GameFolders.TryGetValue(game, out FolderPattern? pattern) ? pattern : LibraryFolders ?? FolderPattern.Default;
    }
}
