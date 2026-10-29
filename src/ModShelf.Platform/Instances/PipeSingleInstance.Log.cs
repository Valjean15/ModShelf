using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Instances;

/// <summary>
/// The single instance's log messages. The launches' arguments are never logged.
/// </summary>
internal sealed partial class PipeSingleInstance
{
    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "ModShelf was launched again, and the launch was handed over to this app")]
    private static partial void LogLaunchedAgain(ILogger logger);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "A launch's message couldn't be read")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning, Message = "Couldn't listen for other launches; trying again")]
    private static partial void LogCantListen(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error, Message = "The app failed while showing itself for another launch")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception);
}
