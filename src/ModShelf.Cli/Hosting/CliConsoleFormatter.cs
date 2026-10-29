using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ModShelf.Cli.Hosting;

/// <summary>
/// Writes each log entry the console shows on one line, such as <c>warn: Couldn't reach example.com</c>, without the
/// exception and its stack trace: a command says in its own words what went wrong, and the log files keep the details.
/// </summary>
internal sealed class CliConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "modshelf";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        textWriter.WriteLine($"{Level(logEntry.LogLevel)}: {message.ReplaceLineEndings(" ").Trim()}");
    }

    // As the console's own formatters name them.
    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => level.ToString().ToLowerInvariant(),
    };
}
