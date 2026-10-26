using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// A read-only copy of a <see cref="DownloadJob"/> at one moment, for the UI and the CLI.
/// Only the scheduler changes jobs, so callers never get the entity itself.
/// </summary>
/// <param name="HandoffPage">
/// While the job is <see cref="DownloadJobStatus.AwaitingHandoff"/>: the page on the source's website where the
/// user starts the download, which then hands the app a link for <see cref="IDownloadService.HandOffAsync"/>.
/// Null otherwise, and also while the scheduler is still asking the source for the page, as it does after a restart.
/// </param>
public sealed record DownloadJobSnapshot(
    DownloadJobId Id,
    ModFileRef File,
    string TargetDirectory,
    string FileName,
    DownloadJobStatus Status,
    long BytesDownloaded,
    long? TotalBytes,
    DownloadFailureReason? FailureReason,
    DateTimeOffset CreatedAt,
    Uri? HandoffPage = null)
{
    public static DownloadJobSnapshot Of(DownloadJob job) => Of(job, handoffPage: null);

    public static DownloadJobSnapshot Of(DownloadJob job, Uri? handoffPage)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new(
            job.Id,
            job.File,
            job.TargetDirectory,
            job.FileName,
            job.Status,
            job.BytesDownloaded,
            job.TotalBytes,
            job.FailureReason,
            job.CreatedAt,
            job.Status == DownloadJobStatus.AwaitingHandoff ? handoffPage : null);
    }
}
