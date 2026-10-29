using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Logging;

/// <summary>
/// One category's logger, writing through its <see cref="FileLoggerProvider"/>. What's enabled is up to the logging
/// configuration's rules for the <c>File</c> provider.
/// </summary>
internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (IsEnabled(logLevel))
        {
            provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}
