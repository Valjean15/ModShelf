namespace ModShelf.Domain.Tests.Downloads;

/// <summary>
/// The status-changing methods of <see cref="Domain.Downloads.DownloadJob"/>, so tests can enumerate them.
/// <see cref="ReportProgress"/> doesn't change the status but is only allowed while downloading.
/// </summary>
public enum DownloadJobAction
{
    StartResolving,
    AwaitHandoff,
    StartDownloading,
    ReportProgress,
    StartVerifying,
    Complete,
    Pause,
    Resume,
    Requeue,
    Fail,
    Retry,
    Cancel,
}
