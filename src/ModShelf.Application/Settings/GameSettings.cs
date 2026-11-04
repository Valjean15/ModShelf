using ModShelf.Domain.Library;

namespace ModShelf.Application.Settings;

/// <summary>
/// A game's own settings, in place of every game's; each left null uses every game's.
/// </summary>
public sealed record GameSettings
{
    /// <summary>
    /// No setting of its own: the game uses every game's.
    /// </summary>
    public static GameSettings None { get; } = new();

    /// <summary>
    /// The folder its files are in, in place of the library's root, as an absolute path. Its files' library paths are
    /// relative to it, and <see cref="Folders"/> still says where they go below it.
    /// </summary>
    /// <exception cref="ArgumentException">The folder isn't an absolute path.</exception>
    public string? Root
    {
        get;
        init
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)))
            {
                throw new ArgumentException($"'{value}' isn't a full path.", nameof(Root));
            }

            field = value;
        }
    }

    /// <summary>
    /// Where its downloaded files go in the library.
    /// </summary>
    public FolderPattern? Folders { get; init; }

    /// <summary>
    /// What happens to its files' old versions when a newer one is downloaded.
    /// </summary>
    public OldVersionRule? OldVersions { get; init; }

    /// <summary>
    /// Whether its watched mods are checked for updates in the background and by checking every game; null for yes. A
    /// check of the game alone checks it either way.
    /// </summary>
    public bool? CheckUpdates { get; init; }

    /// <summary>
    /// How often its watched mods are checked in the background, from <see cref="Domain.Updates.UpdateInterval.Minimum"/>
    /// to <see cref="Domain.Updates.UpdateInterval.Maximum"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The interval is shorter or longer than that.</exception>
    public TimeSpan? UpdateInterval
    {
        get;
        init
        {
            if (value is { } interval)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(interval, Domain.Updates.UpdateInterval.Minimum, nameof(UpdateInterval));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, Domain.Updates.UpdateInterval.Maximum, nameof(UpdateInterval));
            }

            field = value;
        }
    }

    /// <summary>
    /// Whether a background check that finds new updates of its mods shows a notice.
    /// </summary>
    public bool? Notify { get; init; }

    /// <summary>
    /// How Browse shows its mods when it's chosen; null, or <see cref="BrowseDefaults.None"/>, for the screen's own.
    /// </summary>
    public BrowseDefaults? Browse { get; init; }

    /// <summary>
    /// The previews its mods show with where they show as its own: its page on the Library screen, and Browse once it's
    /// chosen. Null for each screen's (<see cref="AppSettings.PreviewFor(PreviewScreen)"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is unknown.</exception>
    public PreviewLevel? Previews
    {
        get;
        init
        {
            if (value is { } level && !Enum.IsDefined(level))
            {
                throw new ArgumentOutOfRangeException(nameof(Previews), level, "The previews must be Off, Thumbnails or Cards.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Whether the desktop app's lists leave it out: the Library's games, the Updates screen and its count, and Browse's
    /// games. Its mods are still checked unless <see cref="CheckUpdates"/> is false. Null for no.
    /// </summary>
    public bool? Hidden { get; init; }

    /// <summary>
    /// Whether the game has no setting of its own.
    /// </summary>
    public bool IsNone => this with { Browse = Browse is { IsNone: true } ? null : Browse } == None;
}
