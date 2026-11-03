namespace ModShelf.Application.Library;

/// <summary>
/// What happened in the library over the last year: files downloaded, with the versions they replaced, archived, deleted
/// and found by rescans, and mods removed.
/// </summary>
public interface ILibraryHistory
{
    /// <summary>
    /// How long the history keeps what happened.
    /// </summary>
    public static TimeSpan Kept { get; } = TimeSpan.FromDays(365);

    /// <summary>
    /// What happened since <paramref name="since"/>, or ever when it's null, the newest first, at most
    /// <paramref name="limit"/> of it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> isn't from 1 to 1,000.</exception>
    Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken);
}
