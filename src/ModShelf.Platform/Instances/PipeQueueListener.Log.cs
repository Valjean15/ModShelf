using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Instances;

/// <summary>
/// The queue's pipe's log messages, as the desktop app writes them. Requests are never logged: they carry links. The
/// requests themselves are logged where they're carried out.
/// </summary>
internal sealed partial class PipeQueueListener
{
    [LoggerMessage(EventId = 1207, Level = LogLevel.Warning, Message = "A queue request couldn't be read")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Warning, Message = "Couldn't listen for queue requests; trying again")]
    private static partial void LogCantListen(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Warning, Message = "A queue request's reply couldn't be sent")]
    private static partial void LogCantReply(ILogger logger, Exception exception);
}
