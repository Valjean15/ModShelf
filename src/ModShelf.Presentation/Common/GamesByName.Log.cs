using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.Common;

/// <summary>
/// The log messages of finding games by name, logged as the screen's that asked.
/// </summary>
internal sealed partial class GamesByName
{
    [LoggerMessage(EventId = 966, Level = LogLevel.Information, Message = "Couldn't find games by name on {Source}: {Reason}")]
    private static partial void LogCantFind(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 967, Level = LogLevel.Warning, Message = "Finding games by name on {Source} failed")]
    private static partial void LogFindFailed(ILogger logger, Exception exception, string source);
}
