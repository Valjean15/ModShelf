using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// A read-only copy of a <see cref="DownloadJob"/> at one moment, for the UI and the CLI.
/// Only the scheduler changes jobs, so callers never get the entity itself.
/// </summary>
public sealed record DownloadJobSnapshot(
    DownloadJobId Id,
    ModFileRef File,
    string TargetDirectory,
    string FileName,
    DownloadJobStatus Status,
    long BytesDownloaded,
    long? TotalBytes,
    DownloadFailureReason? FailureReason,
    DateTimeOffset CreatedAt)
{
    public static DownloadJobSnapshot Of(DownloadJob job)
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
            job.CreatedAt);
    }
}
