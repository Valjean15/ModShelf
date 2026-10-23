namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// Stands in for the terminal's input, typed by the test: a line read waits, as the console does, until the test
/// types one, or the reader is disposed.
/// </summary>
internal sealed class ControlledTextReader : TextReader
{
    private readonly SemaphoreSlim _typed = new(0);
    private readonly Queue<string> _lines = new();
    private readonly CancellationTokenSource _closed = new();

    public void Type(string line)
    {
        lock (_lines)
        {
            _lines.Enqueue(line);
        }

        _typed.Release();
    }

    /// <returns>The next line typed, or null once the reader is disposed.</returns>
    public override string? ReadLine()
    {
        try
        {
            _typed.Wait(_closed.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        lock (_lines)
        {
            return _lines.Dequeue();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A read still waiting ends; the semaphore and token stay, since it may still be using them.
            _closed.Cancel();
        }

        base.Dispose(disposing);
    }
}
