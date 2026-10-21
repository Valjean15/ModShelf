using ModShelf.Domain.Mods;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Domain.Downloads;

/// <summary>
/// One file to download, where it goes, and how far the download has got.
/// </summary>
/// <remarks>
/// <para>
/// The happy path is Queued → Resolving → Downloading → Verifying → Completed. A source that needs the
/// user to start the download on its website goes Resolving → AwaitingHandoff → Resolving first.
/// When a link expires mid-download, the job goes back from Downloading to Resolving for a fresh one.
/// </para>
/// <para>
/// The exits are Paused (by the user; <see cref="Resume"/> requeues it), Failed (by an error;
/// <see cref="Retry"/> requeues it) and Cancelled. Completed and Cancelled are final.
/// <see cref="Requeue"/> puts back in the queue a job whose work was interrupted because the app closed.
/// </para>
/// <para>
/// Every method throws <see cref="InvalidOperationException"/> when the current status doesn't allow it,
/// and then leaves the job unchanged. <see cref="BytesDownloaded"/> survives pauses, restarts and link
/// refreshes, so a download resumes where it stopped.
/// </para>
/// </remarks>
public sealed class DownloadJob
{
    private DownloadJob(DownloadJobId id, ModFileRef file, string targetDirectory, string fileName, DateTimeOffset createdAt)
    {
        Id = id;
        File = file;
        TargetDirectory = targetDirectory;
        FileName = fileName;
        CreatedAt = createdAt;
        Status = Queued;
    }

    public DownloadJobId Id { get; }

    public ModFileRef File { get; }

    /// <summary>
    /// The absolute path of the folder the file is saved to.
    /// </summary>
    public string TargetDirectory { get; }

    /// <summary>
    /// The name the file is saved under inside <see cref="TargetDirectory"/>. Always a single path segment.
    /// </summary>
    public string FileName { get; }

    public DateTimeOffset CreatedAt { get; }

    public DownloadJobStatus Status { get; private set; }

    /// <summary>
    /// How many bytes of the file are on disk.
    /// </summary>
    public long BytesDownloaded { get; private set; }

    /// <summary>
    /// The file's size, when the server reported it.
    /// </summary>
    public long? TotalBytes { get; private set; }

    /// <summary>
    /// Why the job failed. Set only while <see cref="Status"/> is <see cref="DownloadJobStatus.Failed"/>.
    /// </summary>
    public DownloadFailureReason? FailureReason { get; private set; }

    /// <summary>
    /// Whether the job reached a final status: Completed or Cancelled.
    /// </summary>
    public bool IsFinished => Status is Completed or Cancelled;

    /// <summary>
    /// Creates a queued job. Get <paramref name="createdAt"/> from the app's <see cref="TimeProvider"/>.
    /// </summary>
    public static DownloadJob Create(ModFileRef file, string targetDirectory, string fileName, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        if (!Path.IsPathFullyQualified(targetDirectory))
        {
            throw new ArgumentException($"'{targetDirectory}' is not an absolute path.", nameof(targetDirectory));
        }

        FileNames.EnsureSingleSegment(fileName);

        return new DownloadJob(DownloadJobId.New(), file, targetDirectory, fileName, createdAt);
    }

    /// <summary>
    /// Starts asking the source for a download link: for a newly queued job, after the user completed
    /// a handoff, or because the link expired mid-download.
    /// </summary>
    public void StartResolving()
    {
        EnsureStatus(nameof(StartResolving), Queued, AwaitingHandoff, Downloading);
        Status = Resolving;
    }

    /// <summary>
    /// The source needs the user to start the download on its website before it can provide a link.
    /// </summary>
    public void AwaitHandoff()
    {
        EnsureStatus(nameof(AwaitHandoff), Resolving);
        Status = AwaitingHandoff;
    }

    /// <summary>
    /// A link was resolved and the transfer starts.
    /// </summary>
    /// <param name="resumeFromByte">
    /// How many bytes of the file are already on disk, or 0 for a fresh start.
    /// </param>
    /// <param name="totalBytes">The file's size, when the server reported it.</param>
    public void StartDownloading(long resumeFromByte, long? totalBytes)
    {
        EnsureStatus(nameof(StartDownloading), Resolving);
        ArgumentOutOfRangeException.ThrowIfNegative(resumeFromByte);
        if (totalBytes is { } total)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(resumeFromByte, total);
        }

        BytesDownloaded = resumeFromByte;
        TotalBytes = totalBytes;
        Status = Downloading;
    }

    /// <summary>
    /// Records how many bytes of the file are on disk.
    /// </summary>
    public void ReportProgress(long bytesDownloaded)
    {
        EnsureStatus(nameof(ReportProgress), Downloading);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesDownloaded);
        if (TotalBytes is { } total)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bytesDownloaded, total);
        }

        BytesDownloaded = bytesDownloaded;
    }

    /// <summary>
    /// The transfer finished, and the file is being checked against the source's hash.
    /// </summary>
    public void StartVerifying()
    {
        EnsureStatus(nameof(StartVerifying), Downloading);
        Status = Verifying;
    }

    /// <summary>
    /// The file is saved and verified.
    /// </summary>
    public void Complete()
    {
        EnsureStatus(nameof(Complete), Verifying);
        Status = Completed;
    }

    /// <summary>
    /// Stops the job at the user's request. It stays paused, across restarts too, until <see cref="Resume"/>.
    /// </summary>
    public void Pause()
    {
        EnsureStatus(nameof(Pause), Queued, Resolving, AwaitingHandoff, Downloading);
        Status = Paused;
    }

    /// <summary>
    /// Puts a paused job back in the queue. It resolves a fresh link, because links expire,
    /// and continues from <see cref="BytesDownloaded"/>.
    /// </summary>
    public void Resume()
    {
        EnsureStatus(nameof(Resume), Paused);
        Status = Queued;
    }

    /// <summary>
    /// Puts a job whose work was interrupted, because the app closed or crashed mid-step, back in the queue.
    /// A job waiting for a handoff keeps waiting, since the user can still complete it.
    /// </summary>
    public void Requeue()
    {
        EnsureStatus(nameof(Requeue), Resolving, Downloading, Verifying);
        Status = Queued;
    }

    /// <summary>
    /// Stops the job because of an error.
    /// </summary>
    public void Fail(DownloadFailureReason reason)
    {
        EnsureStatus(nameof(Fail), Resolving, AwaitingHandoff, Downloading, Verifying);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown failure reason.");
        }

        FailureReason = reason;
        Status = Failed;
    }

    /// <summary>
    /// Puts a failed job back in the queue.
    /// </summary>
    public void Retry()
    {
        EnsureStatus(nameof(Retry), Failed);
        FailureReason = null;
        Status = Queued;
    }

    /// <summary>
    /// Abandons the job. Deleting a partly downloaded file is up to the caller.
    /// </summary>
    public void Cancel()
    {
        EnsureStatus(nameof(Cancel), Queued, Resolving, AwaitingHandoff, Downloading, Verifying, Paused, Failed);
        FailureReason = null;
        Status = Cancelled;
    }

    private void EnsureStatus(string action, params ReadOnlySpan<DownloadJobStatus> allowed)
    {
        foreach (DownloadJobStatus status in allowed)
        {
            if (status == Status)
            {
                return;
            }
        }

        throw new InvalidOperationException($"{action} isn't allowed while the download job is {Status}.");
    }
}
