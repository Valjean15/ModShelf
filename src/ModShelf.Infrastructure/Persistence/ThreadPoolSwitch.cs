using System.Runtime.CompilerServices;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Awaited, goes on on the thread pool, whatever thread awaited it: <c>await ThreadPoolSwitch.Now;</c>.
/// </summary>
/// <remarks>
/// Each of the library database's repositories starts with it (ADR 0083). Microsoft.Data.Sqlite has no asynchronous
/// I/O: EF Core's <c>…Async</c> methods read and write the file, and wait for another connection's lock, on the thread
/// that calls them, so a caller such as a screen's UI thread would stop until the database answered, up to SQLite's 30
/// seconds. Once switched, the rest of the work stays on the pool, each await being <c>ConfigureAwait(false)</c>, and the
/// caller's own await comes back to its thread as usual.
/// </remarks>
internal readonly struct ThreadPoolSwitch : ICriticalNotifyCompletion
{
    public static ThreadPoolSwitch Now => default;

    /// <summary>
    /// Never: even a caller on the pool goes on in another work item, so the call returns to it at once.
    /// </summary>
    public bool IsCompleted => false;

    public ThreadPoolSwitch GetAwaiter() => this;

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        ThreadPool.QueueUserWorkItem(static run => run(), continuation, preferLocal: false);
    }

    public void UnsafeOnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        ThreadPool.UnsafeQueueUserWorkItem(static run => run(), continuation, preferLocal: false);
    }
}
