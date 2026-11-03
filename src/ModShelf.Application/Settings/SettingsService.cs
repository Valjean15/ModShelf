using System.Collections.Immutable;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Common;
using ModShelf.Domain.Library;
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

    public async Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken)
    {
        string? normalized = root is null ? null : FullRoot(root, nameof(root));
        await ChangeAsync(
                async (settings, token) =>
                {
                    string current = settings.LibraryRoot ?? DefaultLibraryRoot;
                    if (!FilePaths.Same(current, normalized ?? DefaultLibraryRoot))
                    {
                        if ((await _library.ListAsync(token).ConfigureAwait(false)).Any(mod => mod.Files.Count > 0))
                        {
                            throw new InvalidOperationException(
                                $"The library has files in {current}. Moving the library changes its root and takes them along.");
                        }

                        await EnsureNoDownloadsIntoAsync(current, token).ConfigureAwait(false);
                    }

                    return settings with { LibraryRoot = normalized };
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return ChangeAsync((settings, _) => Task.FromResult(settings with { LibraryFolders = pattern }), cancellationToken);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        return SetGameAsync(game, settings => settings with { Folders = pattern }, cancellationToken);
    }

    public Task SetGameAsync(string game, Func<GameSettings, GameSettings> change, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentNullException.ThrowIfNull(change);

        string key = game.Trim();
        return ChangeAsync(
            (settings, _) =>
            {
                GameSettings changed = change(settings.GameFor(key)) ?? throw new InvalidOperationException("A game's changed settings can't be null.");
                if (changed.Browse is { IsNone: true })
                {
                    changed = changed with { Browse = null };
                }

                // The game keeps the key it was stored under, in its case, rather than gaining a second.
                ImmutableDictionary<string, GameSettings> games = settings.Games.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
                string stored = games.Keys.FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) ?? key;
                return Task.FromResult(settings with { Games = changed.IsNone ? games.Remove(stored) : games.SetItem(stored, changed) });
            },
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
    /// Throws unless every download into <paramref name="root"/> has finished or been cancelled.
    /// </summary>
    /// <exception cref="InvalidOperationException">A download into the root is unfinished.</exception>
    internal async Task EnsureNoDownloadsIntoAsync(string root, CancellationToken cancellationToken)
    {
        int unfinished = (await _jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .Count(job => !job.IsFinished && LibraryPaths.RelativeTo(root, Path.Combine(job.TargetDirectory, job.FileName)) is not null);
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
