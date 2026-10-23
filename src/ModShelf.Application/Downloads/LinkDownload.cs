namespace ModShelf.Application.Downloads;

/// <summary>
/// The job <see cref="ILinkDownloads.AddAsync"/> queued or found.
/// </summary>
/// <param name="Job">The job, as it was when added or found.</param>
/// <param name="AlreadyQueued">
/// Whether the job existed already, unfinished, possibly Paused or Failed; if so, nothing new was added.
/// </param>
public sealed record LinkDownload(DownloadJobSnapshot Job, bool AlreadyQueued);
