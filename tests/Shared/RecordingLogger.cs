using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace ModShelf.Testing;

/// <summary>
/// A logger that keeps every entry, so tests can check what was (and wasn't) logged.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    /// Each entry's level, message and exception, as text.
    /// </summary>
    public IReadOnlyCollection<string> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
