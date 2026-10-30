namespace ModShelf.Application.Releases.Ports;

/// <summary>
/// ModShelf's own releases, for an installed ModShelf: finding a newer one, downloading it, and restarting into it. A
/// build from source, or the CLI, isn't installed, and can't update itself.
/// </summary>
public interface IAppReleases
{
    /// <summary>
    /// Whether this ModShelf was installed, so that it can update itself.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// A newer release downloaded already, waiting for the user to restart into it; null for none. Starting ModShelf
    /// again doesn't apply it: only <see cref="RestartToApply"/> does.
    /// </summary>
    AppRelease? Downloaded { get; }

    /// <summary>
    /// Asks where ModShelf is released for a newer release than this one.
    /// </summary>
    /// <returns>The newest release, when it's newer; otherwise null.</returns>
    /// <exception cref="InvalidOperationException">This ModShelf isn't installed.</exception>
    /// <exception cref="HttpRequestException">The releases couldn't be read.</exception>
    /// <exception cref="IOException">The releases couldn't be read.</exception>
    Task<AppRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads <paramref name="release"/>, as <see cref="CheckAsync"/> found it, checking it's whole.
    /// </summary>
    /// <param name="progress">Told how far the download is, from 0 to 100.</param>
    /// <exception cref="InvalidOperationException">This ModShelf isn't installed, or the release wasn't found by a check.</exception>
    /// <exception cref="HttpRequestException">The release couldn't be downloaded.</exception>
    /// <exception cref="IOException">The release couldn't be downloaded, or isn't whole.</exception>
    Task DownloadAsync(AppRelease release, IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Ends this ModShelf, puts the downloaded <paramref name="release"/> in its place, and starts it again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The release isn't downloaded.</exception>
    void RestartToApply(AppRelease release);
}
