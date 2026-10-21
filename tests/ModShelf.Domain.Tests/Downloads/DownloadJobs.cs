using ModShelf.Domain.Downloads;

namespace ModShelf.Domain.Tests.Downloads;

/// <summary>
/// Builds download jobs for tests.
/// </summary>
internal static class DownloadJobs
{
    public const string FileName = "SkyUI_5_2_SE.7z";

    public const long TotalBytes = 1000;

    public static DownloadJob New()
        => DownloadJob.Create(TestData.SkyUiMainFile, TestData.TargetDirectory, FileName, TestData.CreatedAt);

    /// <summary>
    /// Creates a job and walks it to <paramref name="status"/> through allowed transitions only.
    /// A job that reaches Downloading has a known size of <see cref="TotalBytes"/>.
    /// </summary>
    public static DownloadJob In(DownloadJobStatus status)
    {
        DownloadJob job;
        switch (status)
        {
            case DownloadJobStatus.Queued:
                return New();

            case DownloadJobStatus.Resolving:
                job = In(DownloadJobStatus.Queued);
                job.StartResolving();
                break;

            case DownloadJobStatus.AwaitingHandoff:
                job = In(DownloadJobStatus.Resolving);
                job.AwaitHandoff();
                break;

            case DownloadJobStatus.Downloading:
                job = In(DownloadJobStatus.Resolving);
                job.StartDownloading(0, TotalBytes);
                break;

            case DownloadJobStatus.Verifying:
                job = In(DownloadJobStatus.Downloading);
                job.ReportProgress(TotalBytes);
                job.StartVerifying();
                break;

            case DownloadJobStatus.Completed:
                job = In(DownloadJobStatus.Verifying);
                job.Complete();
                break;

            case DownloadJobStatus.Paused:
                job = In(DownloadJobStatus.Queued);
                job.Pause();
                break;

            case DownloadJobStatus.Failed:
                job = In(DownloadJobStatus.Resolving);
                job.Fail(DownloadFailureReason.LinkUnavailable);
                break;

            case DownloadJobStatus.Cancelled:
                job = In(DownloadJobStatus.Queued);
                job.Cancel();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status.");
        }

        Assert.Equal(status, job.Status);
        return job;
    }
}
