using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace ModShelf.Testing;

/// <summary>
/// Records every log entry from every category, including the structured values a log sink would write.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    /// Each entry as "[category] message | name=value, ...".
    /// </summary>
    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
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
            IEnumerable<string> values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(pair => $"{pair.Key}={pair.Value}")
                : [];
            entries.Enqueue($"[{category}] {formatter(state, exception)} | {string.Join(", ", values)} {exception}");
        }
    }
}
