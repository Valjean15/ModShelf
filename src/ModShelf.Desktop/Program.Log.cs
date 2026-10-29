using Microsoft.Extensions.Logging;

namespace ModShelf.Desktop;

/// <summary>
/// The desktop app's startup log messages.
/// </summary>
internal static partial class Program
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Critical, Message = "ModShelf couldn't start")]
    private static partial void LogCantStart(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "ModShelf is running already, so this launch was handed to it")]
    private static partial void LogHandedOver(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "ModShelf is running already, but didn't answer this launch")]
    private static partial void LogNotAnswering(ILogger logger, Exception exception);
}
