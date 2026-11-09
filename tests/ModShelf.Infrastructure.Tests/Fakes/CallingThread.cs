using System.Diagnostics;

namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// A call made on a thread of its own, which is no thread pool's, and which waits, doing nothing else, until disposed.
/// </summary>
internal sealed class CallingThread : IDisposable
{
    private readonly ManualResetEventSlim _called = new();
    private readonly ManualResetEventSlim _released = new();
    private readonly Thread _thread;

    public CallingThread(Func<Task> call)
    {
        _thread = new Thread(() =>
        {
            Id = Environment.CurrentManagedThreadId;
            Stopwatch calling = Stopwatch.StartNew();
            Work = call();
            Calling = calling.Elapsed;
            _called.Set();
            _released.Wait();
        })
        {
            IsBackground = true,
        };
        _thread.Start();
        _called.Wait(TestContext.Current.CancellationToken);
    }

    public int Id { get; private set; }

    public Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// How long the call held the thread before it returned.
    /// </summary>
    public TimeSpan Calling { get; private set; }

    public void Dispose()
    {
        _released.Set();
        _thread.Join();
        _called.Dispose();
        _released.Dispose();
    }
}
