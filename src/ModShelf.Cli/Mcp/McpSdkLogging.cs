using Microsoft.Extensions.Logging;

using ModelContextProtocol;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP SDK's logging, through the host's: its warnings and errors only, and none that carries a message's
/// content, which can hold <c>nxm://</c> links and their keys. A tool's <see cref="McpException"/> is its answer, a
/// problem the agent can act on, not one of ModShelf's, so it isn't logged. ModShelf's own tools log as the rest of
/// ModShelf does.
/// </summary>
internal sealed class McpSdkLogging(ILoggerFactory host) : ILoggerFactory
{
    // What the SDK's log messages name a message's content, or a part of it.
    private static readonly string[] Carried = ["Message", "Request", "Response", "Arguments", "responseContent"];

    public ILogger CreateLogger(string categoryName) => new Logger(host.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider)
        => throw new NotSupportedException("The MCP SDK logs through the host's providers.");

    /// <summary>
    /// Nothing to dispose: the host owns its logging.
    /// </summary>
    public void Dispose()
    {
    }

    private sealed class Logger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)
                || exception is McpException
                || (state is IReadOnlyList<KeyValuePair<string, object?>> values && values.Any(value => Carried.Contains(value.Key, StringComparer.Ordinal))))
            {
                return;
            }

            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
