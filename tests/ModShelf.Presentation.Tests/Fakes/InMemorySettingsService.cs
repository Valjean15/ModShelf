using System.Collections.Immutable;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

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

    public async Task<LibraryRoots> GetLibraryRootsAsync(CancellationToken cancellationToken)
        => LibraryRoots.Of(await GetAsync(cancellationToken), DefaultLibraryRoot);

    public Task SetLibraryRootAsync(string? root, CancellationToken cancellationToken)
        => ChangeAsync($"root {root ?? "null"}", settings => RootRefusal is { } refusal ? throw refusal : settings with { LibraryRoot = root });

    public Task SetLibraryFoldersAsync(GameRef? game, FolderPattern? pattern, CancellationToken cancellationToken)
        => ChangeAsync(
            $"folders {game?.ToString() ?? "*"} {pattern?.Text ?? "null"}",
            settings => game is null ? settings with { LibraryFolders = pattern } : WithGame(settings, game, settings.GameFor(game) with { Folders = pattern }));

    public Task SetGameAsync(GameRef game, Func<GameSettings, GameSettings> change, CancellationToken cancellationToken)
        => ChangeAsync($"game {game}", settings => WithGame(settings, game, change(settings.GameFor(game))));

    public Task CopyGameAsync(GameRef other, GameRef game, CancellationToken cancellationToken)
        => ChangeAsync($"copy {other} to {game}", settings => WithGame(settings, game, settings.GameFor(other)));

    public Task SetOldVersionsAsync(OldVersionRule? rule, CancellationToken cancellationToken)
        => ChangeAsync($"oldVersions {rule?.ToString() ?? "null"}", settings => settings with { OldVersions = rule });

    public Task SetUpdateIntervalAsync(TimeSpan? interval, CancellationToken cancellationToken)
        => ChangeAsync($"interval {interval?.ToString() ?? "null"}", settings => settings with { UpdateInterval = interval });

    public Task SetNotifyOfUpdatesAsync(bool? notify, CancellationToken cancellationToken)
        => ChangeAsync($"notify {notify?.ToString() ?? "null"}", settings => settings with { NotifyOfUpdates = notify });

    public Task SetThemeAsync(AppTheme? theme, CancellationToken cancellationToken)
        => ChangeAsync($"theme {theme?.ToString() ?? "null"}", settings => settings with { Theme = theme });

    public Task SetCloseToTrayAsync(bool? closeToTray, CancellationToken cancellationToken)
        => ChangeAsync($"closeToTray {closeToTray?.ToString() ?? "null"}", settings => settings with { CloseToTray = closeToTray });

    public Task SetSetupDoneAsync(bool? done, CancellationToken cancellationToken)
        => ChangeAsync($"setupDone {done?.ToString() ?? "null"}", settings => settings with { SetupDone = done });

    public Task SetCheckForAppUpdatesAsync(bool? check, CancellationToken cancellationToken)
        => ChangeAsync($"checkForAppUpdates {check?.ToString() ?? "null"}", settings => settings with { CheckForAppUpdates = check });

    public Task SetPreviewAsync(PreviewScreen screen, PreviewLevel? level, CancellationToken cancellationToken)
        => ChangeAsync(
            $"preview {screen} {level?.ToString() ?? "null"}",
            settings => settings with
            {
                Previews = level is { } set
                    ? settings.Previews.ToImmutableDictionary().SetItem(screen, set)
                    : settings.Previews.ToImmutableDictionary().Remove(screen),
            });

    public Task SetPicturesMegabytesAsync(int? megabytes, CancellationToken cancellationToken)
        => ChangeAsync($"picturesMegabytes {megabytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}", settings => settings with { PicturesMegabytes = megabytes });

    public Task SetDetailsMaxAgeAsync(TimeSpan? age, CancellationToken cancellationToken)
        => ChangeAsync($"detailsMaxAge {age?.ToString() ?? "null"}", settings => settings with { DetailsMaxAge = age });

    public Task SetAdultPicturesAsync(AdultPictures? pictures, CancellationToken cancellationToken)
        => ChangeAsync($"adultPictures {pictures?.ToString() ?? "null"}", settings => settings with { AdultPictures = pictures });

    public Task SetQuotaWarnBelowAsync(int? percent, CancellationToken cancellationToken)
        => ChangeAsync($"quotaWarnBelow {percent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}", settings => settings with { QuotaWarnBelow = percent });

    public Task SetAgentsEnabledAsync(bool? enabled, CancellationToken cancellationToken)
        => ChangeAsync($"agentsEnabled {enabled?.ToString() ?? "null"}", settings => settings with { AgentsEnabled = enabled });

    public Task SetAgentsPortAsync(int? port, CancellationToken cancellationToken)
        => ChangeAsync($"agentsPort {port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}", settings => settings with { AgentsPort = port });

    public Task SetBrowserDownloadsFolderAsync(string? folder, CancellationToken cancellationToken)
        => ChangeAsync($"browserDownloadsFolder {folder ?? "null"}", settings => settings with { BrowserDownloadsFolder = folder });

    /// <summary>
    /// The settings with the game's own as <paramref name="own"/>; without the game when it has none.
    /// </summary>
    private static AppSettings WithGame(AppSettings settings, GameRef game, GameSettings own)
    {
        ArgumentNullException.ThrowIfNull(game);
        GameSettings kept = own.Browse is { IsNone: true } ? own with { Browse = null } : own;
        ImmutableDictionary<GameRef, GameSettings> games = settings.Games.ToImmutableDictionary(AppSettings.GameComparer);
        return settings with { Games = kept.IsNone ? games.Remove(game) : games.SetItem(game, kept) };
    }

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
