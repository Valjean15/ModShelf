namespace ModShelf.Application.Downloads;

/// <summary>
/// Carries out requests to change the download queue, in the process that runs it: the desktop app answering another
/// process's request, or that process itself while it runs the queue.
/// </summary>
public interface IQueueRequests
{
    /// <summary>
    /// Carries out <paramref name="request"/>. A problem with it is the reply's, never an exception; only cancelling
    /// <paramref name="cancellationToken"/> throws.
    /// </summary>
    Task<QueueReply> HandleAsync(QueueRequest request, CancellationToken cancellationToken);
}
