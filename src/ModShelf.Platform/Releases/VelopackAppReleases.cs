using ModShelf.Application.Releases;
using ModShelf.Application.Releases.Ports;

using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace ModShelf.Platform.Releases;

/// <summary>
/// ModShelf's releases through Velopack, which installed it: found and downloaded from where ModShelf is released, and
/// put in place by Velopack's updater once ModShelf has ended. Without Velopack's start-up code having run, as in the CLI
/// or a test, or outside an installed ModShelf, as in a build from source, nothing is installed.
/// </summary>
internal sealed class VelopackAppReleases : IAppReleases
{
    private readonly Lazy<UpdateManager?> _manager;
    private UpdateInfo? _found;

    /// <param name="source">Where ModShelf is released.</param>
    /// <param name="locator">Where this ModShelf is installed; null for the one Velopack's start-up code found.</param>
    public VelopackAppReleases(IUpdateSource source, IVelopackLocator? locator = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _manager = new(() => locator is not null || VelopackLocator.IsCurrentSet
            ? new UpdateManager(source, new UpdateOptions(), locator)
            : null);
    }

    public bool IsInstalled => _manager.Value?.IsInstalled == true;

    public AppRelease? Downloaded => IsInstalled && _manager.Value!.UpdatePendingRestart is { } pending ? Of(pending) : null;

    public async Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        UpdateManager manager = Installed();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _found = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or HttpRequestException or IOException))
        {
            throw new IOException($"ModShelf's releases couldn't be read: {exception.Message}", exception);
        }

        return _found is { TargetFullRelease: { } newest } ? Of(newest) : null;
    }

    public async Task DownloadAsync(AppRelease release, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        UpdateManager manager = Installed();
        UpdateInfo found = _found is { } info && Of(info.TargetFullRelease) == release
            ? info
            : throw new InvalidOperationException($"ModShelf {release.Version} wasn't found by a check.");
        try
        {
            await manager.DownloadUpdatesAsync(found, percent => progress?.Report(percent), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or HttpRequestException or IOException))
        {
            throw new IOException($"ModShelf {release.Version} couldn't be downloaded: {exception.Message}", exception);
        }
    }

    public void RestartToApply(AppRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        UpdateManager manager = Installed();
        VelopackAsset pending = manager.UpdatePendingRestart is { } downloaded && Of(downloaded) == release
            ? downloaded
            : throw new InvalidOperationException($"ModShelf {release.Version} isn't downloaded.");

        // The updater waits for this ModShelf to end, which the caller does next, so that it ends as it always does.
        manager.WaitExitThenApplyUpdates(pending, silent: false, restart: true, []);
    }

    private static AppRelease Of(VelopackAsset asset)
        => new(asset.Version.ToString(), string.IsNullOrWhiteSpace(asset.NotesMarkdown) ? null : asset.NotesMarkdown);

    private UpdateManager Installed()
        => _manager.Value is { IsInstalled: true } manager
            ? manager
            : throw new InvalidOperationException("This ModShelf isn't installed, so it can't update itself.");
}
