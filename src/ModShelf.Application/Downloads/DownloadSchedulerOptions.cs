namespace ModShelf.Application.Downloads;

/// <summary>
/// Limits for the download scheduler.
/// </summary>
public sealed record DownloadSchedulerOptions
{
    /// <summary>
    /// How many jobs run at once across all sources.
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 3;

    /// <summary>
    /// How many jobs run at once for any one source, to stay polite to each site.
    /// </summary>
    public int MaxConcurrentDownloadsPerSource { get; init; } = 2;

    /// <summary>
    /// How many times one attempt asks for a fresh ticket after the previous one expired, before failing.
    /// </summary>
    public int MaxLinkRefreshes { get; init; } = 3;

    /// <summary>
    /// The shortest time between two progress snapshots of the same job.
    /// </summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}
