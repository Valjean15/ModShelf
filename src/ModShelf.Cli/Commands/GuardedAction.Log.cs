using Microsoft.Extensions.Logging;

namespace ModShelf.Cli.Commands;

/// <summary>
/// The CLI's log messages for what its commands leave unhandled.
/// </summary>
internal sealed partial class GuardedAction
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Critical, Message = "The command {Command} stopped on something it didn't expect")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string command);
}
