using System.Collections.Immutable;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Settings;

/// <summary>
/// Reads the settings from the <see cref="ISettingsStore"/> each time, so a change made elsewhere, such as by hand, counts
/// at once. Changes go one at a time.
/// </summary>
internal sealed class SettingsService : ISettingsService, IDisposable
{
    private const string LibraryFolderName = "ModShelf";

    private readonly ISettingsStore _store;
    private readonly IUserFolders _folders;
    private readonly ILibraryRepository _library;
    private readonly IDownloadJobRepository _jobs;
    private readonly SemaphoreSlim _changing = new(1, 1);

    public SettingsService(ISettingsStore store, IUserFolders folders, ILibraryRepository library, IDownloadJobRepository jobs)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(jobs);

        _store = store;
        _folders = folders;
        _library = library;
        _jobs = jobs;
    }

    public string Location => _store.Location;

    public string DefaultLibraryRoot => Path.Combine(_folders.Downloads, LibraryFolderName);

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => _store.LoadAsync(cancellationToken);

    public async Task<string> GetLibraryRootAsync(CancellationToken cancellationToken)
        => (await _store.LoadAsync(cancellationToken).ConfigureAwait(false)).LibraryRoot ?? DefaultLibraryRoot;

    public async Task<LibraryRoots> GetLibraryRootsAsync(CancellationToken cancellationToken)
        => LibraryRoots.Of(await _store.LoadAsync(cancellationToken).ConfigureAwait(false), DefaultLibraryRoot);

    public async Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken)
    {
        string? normalized = root is null ? null : FullRoot(root, nameof(root));
        await ChangeAsync(
                async (settings, token) =>
                {
                    string current = settings.LibraryRoot ?? DefaultLibraryRoot;
                    if (!FilePaths.Same(current, normalized ?? DefaultLibraryRoot))
                    {
                        LibraryRoots roots = LibraryRoots.Of(settings, DefaultLibraryRoot);
                        if ((await _library.ListAsync(token).ConfigureAwait(false)).Any(mod => mod.Files.Count > 0 && !roots.HasOwn(mod.Mod.Game)))
                        {
                            throw new InvalidOperationException(
                                $"The library has files in {current}. Moving the library changes its root and takes them along.");
                        }

                        if (roots.ClashOf(null, normalized ?? DefaultLibraryRoot) is { } clash)
                        {
                            throw new InvalidOperationException(clash);
                        }

                        await EnsureNoDownloadsIntoAsync(current, null, token).ConfigureAwait(false);
                    }

                    return settings with { LibraryRoot = normalized };
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetLibraryFoldersAsync(GameRef? game, FolderPattern? pattern, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return ChangeAsync((settings, _) => Task.FromResult(settings with { LibraryFolders = pattern }), cancellationToken);
        }

        return SetGameAsync(game, settings => settings with { Folders = pattern }, cancellationToken);
    }

    public Task SetGameAsync(GameRef game, Func<GameSettings, GameSettings> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(change);

        return ChangeAsync(
            async (settings, token) =>
            {
                GameSettings before = settings.GameFor(game);
                GameSettings after = change(before) ?? throw new InvalidOperationException("A game's changed settings can't be null.");
                // The same folder named another way stays as it's stored.
                after = after with
                {
                    Root = SameFolder(before.Root, after.Root)
                        ? before.Root
                        : await CheckGameRootAsync(settings, game, after.Root, token).ConfigureAwait(false),
                };

                return WithGame(settings, game, after);
            },
            cancellationToken);
    }

    public Task CopyGameAsync(GameRef other, GameRef game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(game);
        if (AppSettings.GameComparer.Equals(other, game))
        {
            throw new ArgumentException("A game can't take its settings from itself.", nameof(game));
        }

        // Its folder changes only with its files.
        return ChangeAsync(
            (settings, _) => Task.FromResult(WithGame(settings, game, settings.GameFor(other) with { Root = settings.GameFor(game).Root })),
            cancellationToken);
    }

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { OldVersions = rule }), cancellationToken);

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken)
    {
        if (interval is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, UpdateInterval.Minimum, nameof(interval));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, UpdateInterval.Maximum, nameof(interval));
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { UpdateInterval = interval }), cancellationToken);
    }

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { NotifyOfUpdates = notify }), cancellationToken);

    public Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken)
    {
        if (theme is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), value, "The theme must be System, Light or Dark.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { Theme = theme }), cancellationToken);
    }

    public Task SetCloseToTrayAsync(bool? closeToTray, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { CloseToTray = closeToTray }), cancellationToken);

    public Task SetSetupDoneAsync(bool? done, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { SetupDone = done }), cancellationToken);

    public Task SetCheckForAppUpdatesAsync(bool? check, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { CheckForAppUpdates = check }), cancellationToken);

    public Task SetPreviewAsync(PreviewScreen screen, PreviewLevel? level, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(screen))
        {
            throw new ArgumentOutOfRangeException(nameof(screen), screen, "The screen must be Library, Updates, Browse or Downloads.");
        }

        if (level is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(level), value, "The previews must be Off, Thumbnails or Cards.");
        }

        return ChangeAsync(
            (settings, _) =>
            {
                ImmutableDictionary<PreviewScreen, PreviewLevel> previews = settings.Previews.ToImmutableDictionary();
                return Task.FromResult(settings with { Previews = level is { } set ? previews.SetItem(screen, set) : previews.Remove(screen) });
            },
            cancellationToken);
    }

    public Task SetPicturesMegabytesAsync(int? megabytes, CancellationToken cancellationToken)
    {
        if (megabytes is < AppSettings.MinPicturesMegabytes or > AppSettings.MaxPicturesMegabytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(megabytes),
                megabytes,
                $"Saved pictures can take from {AppSettings.MinPicturesMegabytes} to {AppSettings.MaxPicturesMegabytes} MB.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { PicturesMegabytes = megabytes }), cancellationToken);
    }

    public Task SetDetailsMaxAgeAsync(TimeSpan? age, CancellationToken cancellationToken)
    {
        if (age is { } value && (value < UpdateInterval.Minimum || value > UpdateInterval.Maximum))
        {
            throw new ArgumentOutOfRangeException(
                nameof(age),
                value,
                $"Mod details can be kept from {UpdateInterval.Format(UpdateInterval.Minimum)} to {UpdateInterval.Format(UpdateInterval.Maximum)}.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { DetailsMaxAge = age }), cancellationToken);
    }

    public Task SetQuotaWarnBelowAsync(int? percent, CancellationToken cancellationToken)
    {
        if (percent is < 0 or > AppSettings.MaxQuotaWarnBelow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent),
                percent,
                $"The warning can be from 0, for never, to {AppSettings.MaxQuotaWarnBelow} percent.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { QuotaWarnBelow = percent }), cancellationToken);
    }

    public Task SetAgentsEnabledAsync(bool? enabled, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { AgentsEnabled = enabled }), cancellationToken);

    public Task SetAgentsPortAsync(int? port, CancellationToken cancellationToken)
    {
        if (port is < AppSettings.MinAgentsPort or > AppSettings.MaxAgentsPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                $"The port can be from {AppSettings.MinAgentsPort} to {AppSettings.MaxAgentsPort}.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { AgentsPort = port }), cancellationToken);
    }

    public Task SetAdultPicturesAsync(AdultPictures? pictures, CancellationToken cancellationToken)
    {
        if (pictures is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(pictures), value, "Adult pictures must be Hidden, Blurred or Shown.");
        }

        return ChangeAsync((settings, _) => Task.FromResult(settings with { AdultPictures = pictures }), cancellationToken);
    }

    public void Dispose() => _changing.Dispose();

    /// <summary>
    /// Sets the root of a library whose files are there already, as moving the library does once they're all moved.
    /// </summary>
    internal Task MoveLibraryRootAsync(string root, CancellationToken cancellationToken)
    {
        string normalized = FullRoot(root, nameof(root));
        return ChangeAsync(
            (settings, _) => Task.FromResult(settings with { LibraryRoot = normalized }),
            cancellationToken);
    }

    /// <summary>
    /// Sets the folder of a game whose files are there already, as moving the game does once they're all moved; null
    /// for the library's root.
    /// </summary>
    internal Task MoveGameRootAsync(GameRef game, string? root, CancellationToken cancellationToken)
    {
        string? normalized = root is null ? null : FullRoot(root, nameof(root));
        return ChangeAsync(
            (settings, _) => Task.FromResult(WithGame(settings, game, settings.GameFor(game) with { Root = normalized })),
            cancellationToken);
    }

    /// <summary>
    /// Throws unless every download into <paramref name="root"/>, of <paramref name="game"/>'s files when it's given,
    /// has finished or been cancelled.
    /// </summary>
    /// <exception cref="InvalidOperationException">Such a download is unfinished.</exception>
    internal async Task EnsureNoDownloadsIntoAsync(string root, GameRef? game, CancellationToken cancellationToken)
    {
        int unfinished = (await _jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .Count(job => !job.IsFinished
                && (game is null || AppSettings.GameComparer.Equals(job.File.Mod.Game, game))
                && LibraryPaths.RelativeTo(root, Path.Combine(job.TargetDirectory, job.FileName)) is not null);
        if (unfinished > 0)
        {
            throw new InvalidOperationException(unfinished == 1
                ? $"A download into {root} isn't finished. Let it finish, or cancel it, first."
                : $"{unfinished} downloads into {root} aren't finished. Let them finish, or cancel them, first.");
        }
    }

    /// <summary>
    /// A root as it's stored: a full path, without an ending separator.
    /// </summary>
    /// <exception cref="ArgumentException">It isn't a full path.</exception>
    internal string FullRoot(string root, string paramName)
    {
        if (!Path.IsPathFullyQualified(root.Trim()))
        {
            throw new ArgumentException($"'{root}' isn't a full path, such as {DefaultLibraryRoot}.", paramName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
    }

    private static bool SameFolder(string? first, string? second)
        => first is null ? second is null : second is not null && FilePaths.Same(first, second);

    /// <summary>
    /// The settings with <paramref name="game"/>'s own as <paramref name="own"/>: kept under the key it was stored under,
    /// in its case, rather than gaining a second; forgotten when it has none.
    /// </summary>
    private static AppSettings WithGame(AppSettings settings, GameRef game, GameSettings own)
    {
        GameSettings changed = own.Browse is { IsNone: true } ? own with { Browse = null } : own;
        ImmutableDictionary<GameRef, GameSettings> games = settings.Games.ToImmutableDictionary(AppSettings.GameComparer);
        GameRef stored = games.Keys.FirstOrDefault(key => AppSettings.GameComparer.Equals(key, game)) ?? game;
        return settings with { Games = changed.IsNone ? games.Remove(stored) : games.SetItem(stored, changed) };
    }

    /// <summary>
    /// The new folder of a game whose folder changes without its files, as it's stored, once it's checked.
    /// </summary>
    /// <returns>The folder as it's stored; null for the library's root.</returns>
    /// <exception cref="InvalidOperationException">The game has files, downloads into its folder aren't finished, or the folder clashes.</exception>
    private async Task<string?> CheckGameRootAsync(AppSettings settings, GameRef game, string? root, CancellationToken cancellationToken)
    {
        string? normalized = root is null ? null : FullRoot(root, nameof(root));
        LibraryRoots roots = LibraryRoots.Of(settings, DefaultLibraryRoot);
        string current = roots.For(game);
        if ((await _library.ListAsync(cancellationToken).ConfigureAwait(false))
            .Any(mod => mod.Files.Count > 0 && AppSettings.GameComparer.Equals(mod.Mod.Game, game)))
        {
            throw new InvalidOperationException($"{game} has files in {current}. Moving the game's files changes its folder and takes them along.");
        }

        if (normalized is not null && roots.ClashOf(game, normalized) is { } clash)
        {
            throw new InvalidOperationException(clash);
        }

        await EnsureNoDownloadsIntoAsync(current, game, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    private async Task ChangeAsync(Func<AppSettings, CancellationToken, Task<AppSettings>> change, CancellationToken cancellationToken)
    {
        await _changing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(await change(settings, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _changing.Release();
        }
    }
}
