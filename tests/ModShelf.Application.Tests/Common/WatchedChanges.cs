using ModShelf.Application.Library;

namespace ModShelf.Application.Tests.Common;

/// <summary>
/// Watches the library's changes from when it's made, for a test to read once it has done what it tests.
/// </summary>
internal sealed class WatchedChanges : IDisposable
{
    private readonly CancellationTokenSource _watching = new();
    private readonly IAsyncEnumerable<LibraryChange> _changes;

    public WatchedChanges(LibraryChanges changes)
    {
        _changes = changes.Watch(_watching.Token);
    }

    public WatchedChanges(IModLibrary library)
    {
        _changes = library.ChangesAsync(_watching.Token);
    }

    /// <summary>
    /// Stops watching, and returns every change published meanwhile, in order.
    /// </summary>
    public async Task<IReadOnlyList<LibraryChange>> StopAsync()
    {
        await _watching.CancelAsync();
        List<LibraryChange> seen = [];
        await foreach (LibraryChange change in _changes)
        {
            seen.Add(change);
        }

        return seen;
    }

    public void Dispose() => _watching.Dispose();
}
