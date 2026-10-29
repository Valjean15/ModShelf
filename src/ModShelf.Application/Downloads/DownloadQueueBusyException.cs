namespace ModShelf.Application.Downloads;

/// <summary>
/// Another process, such as the desktop app or another CLI command, runs the library's download queue now, so this one
/// can't add or change jobs until it lets go (<see cref="IDownloadService.IsQueueBusyElsewhere"/>).
/// </summary>
public sealed class DownloadQueueBusyException : InvalidOperationException
{
    public DownloadQueueBusyException()
        : base("Another ModShelf process runs the downloads of this library.")
    {
    }

    public DownloadQueueBusyException(string message)
        : base(message)
    {
    }

    public DownloadQueueBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
