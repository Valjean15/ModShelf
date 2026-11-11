using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Downloads;

/// <summary>
/// A change to the download queue, asked of the process that runs it, such as by an agent through ModShelf's MCP
/// server while the desktop app runs the queue (ADR 0040).
/// </summary>
/// <param name="Kind">What it asks.</param>
/// <param name="Link">For <see cref="QueueRequestKind.Download"/>, the link. It can carry a key, so it's never logged.</param>
/// <param name="Job">For the other kinds, the download it's about.</param>
/// <param name="Path">For <see cref="QueueRequestKind.TakeFile"/>, the file's full path. It can hold the user's name, so it's never logged.</param>
public sealed record QueueRequest(QueueRequestKind Kind, Uri? Link = null, DownloadJobId? Job = null, string? Path = null)
{
    public static QueueRequest Download(Uri link) => new(QueueRequestKind.Download, Link: link);

    public static QueueRequest Pause(DownloadJobId job) => new(QueueRequestKind.Pause, Job: job);

    public static QueueRequest Resume(DownloadJobId job) => new(QueueRequestKind.Resume, Job: job);

    public static QueueRequest Retry(DownloadJobId job) => new(QueueRequestKind.Retry, Job: job);

    public static QueueRequest Cancel(DownloadJobId job) => new(QueueRequestKind.Cancel, Job: job);

    public static QueueRequest TakeFile(DownloadJobId job, string path) => new(QueueRequestKind.TakeFile, Job: job, Path: path);
}
