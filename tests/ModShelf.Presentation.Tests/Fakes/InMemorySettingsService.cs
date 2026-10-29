using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Settings kept in memory, changed as the real service changes them, recording each change. The test can make reading
/// or saving fail, and hold changes until it lets them go.
/// </summary>
internal sealed class InMemorySettingsService : ISettingsService
{
    private readonly Lock _gate = new();
    private readonly List<string> _changes = [];

    public string Location { get; } = Path.Combine(Path.GetTempPath(), "ModShelf", "settings.json");

    public string DefaultLibraryRoot => TestData.LibraryRoot;

    /// <summary>
    /// The settings as stored.
    /// </summary>
    public AppSettings Stored { get; set; } = new();

    /// <summary>
    /// What reading the settings, and so changing them, throws while set, as for a file that isn't JSON.
    /// </summary>
    public InvalidDataException? ReadRefusal { get; set; }

    /// <summary>
    /// What saving a change throws while set, as for a full disk.
    /// </summary>
    public Exception? SaveRefusal { get; set; }

    /// <summary>
    /// What setting the library's root throws while set, as while downloads into it run.
    /// </summary>
    public Exception? RootRefusal { get; set; }

    /// <summary>
    /// While set, each change waits for it before it's made.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// Each change asked for, such as "theme Dark" or "oldVersions null", oldest first, made or not.
    /// </summary>
    public IReadOnlyList<string> Changes
    {
        get
        {
            lock (_gate)
            {
                return [.. _changes];
            }
        }
    }

    public Task<AppSettings> GetAsync(CancellationToken cancellationToken)
        => ReadRefusal is { } refusal ? Task.FromException<AppSettings>(refusal) : Task.FromResult(Stored);

    public async Task<string> GetLibraryRootAsync(CancellationToken cancellationToken)
        => (await GetAsync(cancellationToken)).LibraryRoot ?? DefaultLibraryRoot;

    public Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken)
        => ChangeAsync($"root {root ?? "null"}", settings => RootRefusal is { } refusal ? throw refusal : settings with { LibraryRoot = root });

    public Task SetLibraryFoldersAsync(string? game, FolderPattern? pattern, CancellationToken cancellationToken)
        => ChangeAsync(
            $"folders {game ?? "*"} {pattern?.Text ?? "null"}",
            settings =>
            {
                if (game is null)
                {
                    return settings with { LibraryFolders = pattern };
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(game);
                ImmutableDictionary<string, FolderPattern> games = settings.GameFolders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase).Remove(game);
                return settings with { GameFolders = pattern is null ? games : games.Add(game, pattern) };
            });

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken)
        => ChangeAsync($"oldVersions {rule?.ToString() ?? "null"}", settings => settings with { OldVersions = rule });

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken)
        => ChangeAsync($"interval {interval?.ToString() ?? "null"}", settings => settings with { UpdateInterval = interval });

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken)
        => ChangeAsync($"notify {notify?.ToString() ?? "null"}", settings => settings with { NotifyOfUpdates = notify });

    public Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken)
        => ChangeAsync($"theme {theme?.ToString() ?? "null"}", settings => settings with { Theme = theme });

    private async Task ChangeAsync(string change, Func<AppSettings, AppSettings> apply)
    {
        lock (_gate)
        {
            _changes.Add(change);
        }

        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        if (ReadRefusal is { } unreadable)
        {
            throw unreadable;
        }

        if (SaveRefusal is { } refusal)
        {
            throw refusal;
        }

        Stored = apply(Stored);
    }
}
