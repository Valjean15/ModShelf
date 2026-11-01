using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Instances;

/// <summary>
/// The queue's pipe's log messages, as the asking side writes them. Requests are never logged: they carry links.
/// </summary>
internal sealed partial class PipeQueueOwner
{
    [LoggerMessage(EventId = 1204, Level = LogLevel.Debug, Message = "No desktop app takes queue requests for this library")]
    private static partial void LogNoDesktopApp(ILogger logger);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Warning, Message = "The desktop app took a queue request, but didn't reply")]
    private static partial void LogNoReply(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "The queue's pipe is another user's, so it isn't asked")]
    private static partial void LogNotThisUsers(ILogger logger, Exception exception);
}
