using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Updates;

/// <summary>
/// The background update checks' log messages.
/// </summary>
internal sealed partial class UpdateCheckService
{
    [LoggerMessage(EventId = 703, Level = LogLevel.Error, Message = "The background check for updates failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 704, Level = LogLevel.Warning, Message = "Couldn't read how often to check for updates, so it's every 6 hours")]
    private static partial void LogCantReadInterval(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 705, Level = LogLevel.Warning, Message = "Couldn't show the notice of new updates ({UpdateCount} found)")]
    private static partial void LogCantNotify(ILogger logger, Exception exception, int updateCount);
}
