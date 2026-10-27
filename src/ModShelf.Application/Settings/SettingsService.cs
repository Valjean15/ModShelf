using System.Collections.Immutable;

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
    private readonly SemaphoreSlim _changing = new(1, 1);

    public SettingsService(ISettingsStore store, IUserFolders folders, ILibraryRepository library)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(library);

        _store = store;
        _folders = folders;
        _library = library;
    }

    public string Location => _store.Location;

    public string DefaultLibraryRoot => Path.Combine(_folders.Downloads, LibraryFolderName);

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => _store.LoadAsync(cancellationToken);

    public async Task<string> GetLibraryRootAsync(CancellationToken cancellationToken)
        => (await _store.LoadAsync(cancellationToken).ConfigureAwait(false)).LibraryRoot ?? DefaultLibraryRoot;

    public async Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken)
    {
        string? normalized = null;
        if (root is not null)
        {
            if (!Path.IsPathFullyQualified(root.Trim()))
            {
                throw new ArgumentException($"'{root}' isn't a full path, such as {Path.Combine(_folders.Downloads, LibraryFolderName)}.", nameof(root));
            }

            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
        }

        await ChangeAsync(
                async (settings, token) =>
                {
                    string current = settings.LibraryRoot ?? DefaultLibraryRoot;
                    if (!SamePath(current, normalized ?? DefaultLibraryRoot)
                        && (await _library.ListAsync(token).ConfigureAwait(false)).Any(mod => mod.Files.Count > 0))
                    {
                        throw new InvalidOperationException(
                            $"The library has files in {current}. Until ModShelf can move them, its root can only change while it has none.");
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

    public void Dispose() => _changing.Dispose();

    /// <summary>
    /// Whether two paths name the same folder: ignoring case on Windows and macOS, whose usual file systems do.
    /// </summary>
    private static bool SamePath(string first, string second)
        => string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
