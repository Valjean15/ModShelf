using System.Collections.Immutable;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Library;

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
                    if (!SamePath(current, normalized ?? DefaultLibraryRoot))
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

    public async Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken)
    {
        if (game is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(game);
        }

        await ChangeAsync(
                (settings, _) =>
                {
                    if (game is null)
                    {
                        return Task.FromResult(settings with { LibraryFolders = pattern });
                    }

                    ImmutableDictionary<string, FolderPattern> games = settings.GameFolders
                        .ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
                        .Remove(game.Trim());
                    return Task.FromResult(settings with { GameFolders = pattern is null ? games : games.Add(game.Trim(), pattern) });
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken)
        => ChangeAsync((settings, _) => Task.FromResult(settings with { OldVersions = rule }), cancellationToken);

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
    /// Whether two paths name the same folder: ignoring case on Windows and macOS, whose usual file systems do.
    /// </summary>
    private static bool SamePath(string first, string second)
        => string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// A root as it's stored: a full path, without an ending separator.
    /// </summary>
    /// <exception cref="ArgumentException">It isn't a full path.</exception>
    private string FullRoot(string root, string paramName)
    {
        if (!Path.IsPathFullyQualified(root.Trim()))
        {
            throw new ArgumentException($"'{root}' isn't a full path, such as {Path.Combine(_folders.Downloads, LibraryFolderName)}.", paramName);
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
