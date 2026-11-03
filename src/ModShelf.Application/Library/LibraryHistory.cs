using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;

namespace ModShelf.Application.Library;

/// <summary>
/// The library's history: lists what happened, and keeps what the library's services say happened, forgetting what's
/// older than <see cref="ILibraryHistory.Kept"/>.
/// </summary>
internal sealed partial class LibraryHistory : ILibraryHistory
{
    /// <summary>
    /// The most a listing returns.
    /// </summary>
    public const int MaxLimit = 1000;

    private readonly ILibraryHistoryRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryHistory> _logger;

    public LibraryHistory(ILibraryHistoryRepository repository, TimeProvider timeProvider, ILogger<LibraryHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);

        return _repository.ListAsync(since, limit, cancellationToken);
    }

    /// <summary>
    /// Keeps what happened. The history is told, not asked: when it can't keep it, which is logged, the library is as it
    /// was told it is, and only its history lacks it.
    /// </summary>
    public async Task RecordAsync(IReadOnlyList<LibraryEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        try
        {
            await _repository.AddAsync(events, _timeProvider.GetUtcNow() - ILibraryHistory.Kept, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCantRecord(_logger, exception, events.Count);
        }
    }
}
