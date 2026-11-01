namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// The desktop app running for this library, which runs its download queue, reached from another process such as the
/// CLI's MCP server. Platform reaches it over a pipe only the same user can open.
/// </summary>
public interface IQueueOwner
{
    /// <summary>
    /// Asks the desktop app to carry out <paramref name="request"/>.
    /// </summary>
    /// <returns>Its reply; or null when no desktop app runs for this library, or one too old to take requests.</returns>
    /// <exception cref="TimeoutException">
    /// The desktop app took the request, but didn't reply in time, or closed without replying: it may have carried it out.
    /// </exception>
    /// <exception cref="InvalidDataException">Its reply couldn't be read, as from a newer app.</exception>
    Task<QueueReply?> SendAsync(QueueRequest request, CancellationToken cancellationToken);
}
