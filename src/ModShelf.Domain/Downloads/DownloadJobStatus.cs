namespace ModShelf.Domain.Downloads;

/// <summary>
/// Where a <see cref="DownloadJob"/> stands. See <see cref="DownloadJob"/> for the allowed transitions.
/// </summary>
/// <remarks>The numeric values are stored, so never renumber them.</remarks>
public enum DownloadJobStatus
{
    /// <summary>Waiting for the scheduler to pick the job up.</summary>
    Queued = 0,

    /// <summary>Asking the source for a download link.</summary>
    Resolving = 1,

    /// <summary>Waiting for the user to start the download on the source's website, such as Nexus Mods for free accounts.</summary>
    AwaitingHandoff = 2,

    /// <summary>Transferring the file.</summary>
    Downloading = 3,

    /// <summary>Checking the downloaded file against the hash the source published.</summary>
    Verifying = 4,

    /// <summary>The file is saved and verified. Final.</summary>
    Completed = 5,

    /// <summary>Stopped by the user; stays paused across restarts until resumed.</summary>
    Paused = 6,

    /// <summary>Stopped by an error; see <see cref="DownloadJob.FailureReason"/>. Can be retried.</summary>
    Failed = 7,

    /// <summary>Abandoned by the user. Final.</summary>
    Cancelled = 8,
}
