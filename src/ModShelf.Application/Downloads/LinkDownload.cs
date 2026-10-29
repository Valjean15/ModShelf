namespace ModShelf.Application.Downloads;

/// <summary>
/// The job <see cref="ILinkDownloads.AddAsync"/> queued or found.
/// </summary>
/// <param name="Job">The job, as it was when added or found.</param>
/// <param name="AlreadyQueued">
/// Whether the job existed already, unfinished, possibly Paused or Failed; if so, nothing new was added.
/// </param>
/// <param name="AlreadySaved">
/// Whether a finished job saved the file there already, and it's still there; if so, <paramref name="Job"/> is that
/// job, nothing new was added, and nothing downloads.
/// </param>
public sealed record LinkDownload(DownloadJobSnapshot Job, bool AlreadyQueued, bool AlreadySaved = false);
