using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Downloads;

internal sealed partial class QueueRequests
{
    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "A queue request: {Kind}")]
    private static partial void LogRequest(ILogger logger, QueueRequestKind kind);

    [LoggerMessage(EventId = 21, Level = LogLevel.Error, Message = "A queue request failed: {Kind}")]
    private static partial void LogFailed(ILogger logger, Exception exception, QueueRequestKind kind);
}
