namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// Lets one process at a time run a library's download queue, such as the desktop app or the CLI's download command:
/// two at once would each start, and requeue, the other's jobs. Infrastructure keeps it next to the library database.
/// </summary>
/// <remarks>
/// The process holds it until it releases it or ends, however it ends, so a crash never leaves the queue locked.
/// </remarks>
public interface IDownloadQueueLock
{
    /// <summary>
    /// Takes the lock, unless another process holds it.
    /// </summary>
    /// <returns>Whether this process holds the lock now: true again when it held it already.</returns>
    bool TryAcquire();

    /// <summary>
    /// Lets the lock go, when this process holds it.
    /// </summary>
    void Release();
}
