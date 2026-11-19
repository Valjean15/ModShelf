using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// The download speed limit's log messages.
/// </summary>
internal sealed partial class DownloadSpeedLimiter
{
    [LoggerMessage(EventId = 112, Level = LogLevel.Information, Message = "Downloads are limited to {BytesPerSecond} bytes a second together")]
    private static partial void LogLimited(ILogger logger, long bytesPerSecond);

    [LoggerMessage(EventId = 113, Level = LogLevel.Information, Message = "Downloads are no longer limited")]
    private static partial void LogUnlimited(ILogger logger);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Couldn't read the download speed limit, so the one before stays")]
    private static partial void LogLimitUnread(ILogger logger, Exception exception);
}
