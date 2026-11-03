using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// The library's history in a list, the oldest first, as it was kept.
/// </summary>
internal sealed class InMemoryLibraryHistory : ILibraryHistoryRepository
{
    private readonly Lock _gate = new();
    private readonly List<LibraryEvent> _events = [];

    /// <summary>
    /// What keeping events throws, such as a database that's busy; null for nothing.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// What was kept, the oldest first.
    /// </summary>
    public IReadOnlyList<LibraryEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>
    /// The last time to keep from that adding said.
    /// </summary>
    public DateTimeOffset? KeptSince { get; private set; }

    /// <summary>
    /// A history over this list, on <paramref name="timeProvider"/>'s clock.
    /// </summary>
    public LibraryHistory History(TimeProvider timeProvider) => new(this, timeProvider, new RecordingLogger<LibraryHistory>());

    public Task AddAsync(IReadOnlyList<LibraryEvent> events, DateTimeOffset keepSince, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        lock (_gate)
        {
            _events.AddRange(events);
            _events.RemoveAll(happened => happened.At < keepSince);
            KeptSince = keepSince;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LibraryEvent>>(
                [.. _events.Select((happened, order) => (happened, order))
                    .Where(entry => since is null || entry.happened.At >= since)
                    .OrderByDescending(entry => entry.happened.At)
                    .ThenBy(entry => entry.order)
                    .Take(limit)
                    .Select(entry => entry.happened)]);
        }
    }
}
