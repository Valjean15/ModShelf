namespace ModShelf.Application.Library.Ports;

/// <summary>
/// Stores what happened in the library, so the history survives restarts. Implemented by Infrastructure.
/// </summary>
public interface ILibraryHistoryRepository
{
    /// <summary>
    /// Keeps <paramref name="events"/>, and forgets what happened before <paramref name="keepSince"/>.
    /// </summary>
    Task AddAsync(IReadOnlyList<LibraryEvent> events, DateTimeOffset keepSince, CancellationToken cancellationToken);

    /// <summary>
    /// What happened since <paramref name="since"/>, or ever when it's null, the newest first, at most
    /// <paramref name="limit"/> of it; what happened at the same time, in the order it was kept.
    /// </summary>
    Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken);
}
