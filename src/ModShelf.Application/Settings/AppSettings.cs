using System.Collections.Immutable;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

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
    /// Below what share of a site's allowance of requests the desktop app warns, in percent, when
    /// <see cref="QuotaWarnBelow"/> isn't set.
    /// </summary>
    public const int DefaultQuotaWarnBelow = 10;

    /// <summary>
    /// The highest share of an allowance <see cref="QuotaWarnBelow"/> can be, in percent; 0 turns the warning off.
    /// </summary>
    public const int MaxQuotaWarnBelow = 90;

    /// <summary>
    /// The port of the address agents reach ModShelf's MCP server at, on this computer alone, when
    /// <see cref="AgentsPort"/> isn't set.
    /// </summary>
    public const int DefaultAgentsPort = 47823;

    /// <summary>
    /// The lowest port <see cref="AgentsPort"/> can be: below it, ports are the system's.
    /// </summary>
    public const int MinAgentsPort = 1024;

    /// <summary>
    /// The highest port <see cref="AgentsPort"/> can be.
    /// </summary>
    public const int MaxAgentsPort = 65535;

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
    /// Single games' own settings, by the game: its site, and its key there in any case (<see cref="GameComparer"/>). The
    /// same game on two sites is two games, each with settings of its own.
    /// </summary>
    public IReadOnlyDictionary<GameRef, GameSettings> Games { get; init; }
        = ImmutableDictionary.Create<GameRef, GameSettings>(GameComparer);

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
    /// Below what share of a site's allowance of requests, an hour's or a day's, the desktop app warns that they're running
    /// low, in percent from 0, for never, to <see cref="MaxQuotaWarnBelow"/>; null for <see cref="DefaultQuotaWarnBelow"/>.
    /// </summary>
    public int? QuotaWarnBelow { get; init; }

    /// <summary>
    /// Whether AI agents may use ModShelf through its MCP server, which the user turns on; null for no. While it's off, the
    /// server answers every call saying so, and the desktop app doesn't serve the address.
    /// </summary>
    public bool? AgentsEnabled { get; init; }

    /// <summary>
    /// The port of the address the desktop app serves the MCP server at while agents may use ModShelf, from
    /// <see cref="MinAgentsPort"/> to <see cref="MaxAgentsPort"/>; null for <see cref="DefaultAgentsPort"/>.
    /// </summary>
    public int? AgentsPort { get; init; }

    /// <summary>
    /// The folder the browser saves downloads to, a full path, where ModShelf looks for the file of a download waiting on
    /// a site's page; null for the system's Downloads folder.
    /// </summary>
    public string? BrowserDownloadsFolder { get; init; }

    /// <summary>
    /// The sites' games picked for games installed on this computer: by the installed game's store and ID
    /// (<see cref="Installed.Ports.InstalledGame.Key"/>, such as "steam/489830"), then by site, the game's key there, or
    /// null when the installed game has none there. A site not named matches the installed game by its name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<SourceId, string?>> InstalledMatches { get; init; }
        = ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>>.Empty;

    /// <summary>
    /// The previews <paramref name="screen"/> shows: as set, or its default, thumbnails on the Library screen, cards on
    /// Browse, and none on the others.
    /// </summary>
    public PreviewLevel PreviewFor(PreviewScreen screen)
        => Previews.TryGetValue(screen, out PreviewLevel level) ? level : DefaultPreview(screen);

    /// <summary>
    /// The previews <paramref name="screen"/> shows <paramref name="game"/>'s mods with: the game's own, on the Library
    /// screen and Browse, or the screen's.
    /// </summary>
    public PreviewLevel PreviewFor(PreviewScreen screen, GameRef game)
        => screen is PreviewScreen.Library or PreviewScreen.Browse && GameFor(game).Previews is { } own ? own : PreviewFor(screen);

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
    /// Compares games as the settings do: the same site, and the same key in any case, as a user may type it.
    /// </summary>
    public static IEqualityComparer<GameRef> GameComparer { get; } = new SameGame();

    /// <summary>
    /// The settings of <paramref name="game"/>'s own: <see cref="GameSettings.None"/> when it has none.
    /// </summary>
    public GameSettings GameFor(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return Games.TryGetValue(game, out GameSettings? settings)
            ? settings
            : Games.FirstOrDefault(stored => GameComparer.Equals(stored.Key, game)).Value ?? GameSettings.None;
    }

    /// <summary>
    /// The pattern files of <paramref name="game"/> go by: its own, or every game's.
    /// </summary>
    public FolderPattern FoldersFor(GameRef game) => GameFor(game).Folders ?? LibraryFolders ?? FolderPattern.Default;

    /// <summary>
    /// What happens to old versions of <paramref name="game"/>'s files: its own rule, or every game's.
    /// </summary>
    public OldVersionRule OldVersionsFor(GameRef game) => GameFor(game).OldVersions ?? OldVersions ?? OldVersionRule.Default;

    /// <summary>
    /// How often <paramref name="game"/>'s watched mods are checked in the background: its own interval, or every
    /// game's.
    /// </summary>
    public TimeSpan IntervalFor(GameRef game) => GameFor(game).UpdateInterval ?? UpdateInterval ?? Domain.Updates.UpdateInterval.Default;

    /// <summary>
    /// Whether a background check that finds new updates of <paramref name="game"/>'s mods shows a notice: as the game
    /// says, or as every game does.
    /// </summary>
    public bool NotifiesFor(GameRef game) => GameFor(game).Notify ?? NotifyOfUpdates ?? true;

    /// <summary>
    /// Whether <paramref name="game"/>'s watched mods are checked for updates with every game's: yes unless it says not.
    /// </summary>
    public bool ChecksUpdatesOf(GameRef game) => GameFor(game).CheckUpdates ?? true;

    /// <summary>
    /// Whether the desktop app's lists leave <paramref name="game"/> out.
    /// </summary>
    public bool IsHidden(GameRef game) => GameFor(game).Hidden ?? false;

    private sealed class SameGame : IEqualityComparer<GameRef>
    {
        public bool Equals(GameRef? x, GameRef? y)
            => ReferenceEquals(x, y)
                || (x is not null && y is not null && x.Source == y.Source && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase));

        public int GetHashCode(GameRef obj)
            => HashCode.Combine(obj.Source, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
