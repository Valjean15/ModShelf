using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Notifications;

internal sealed partial class NoticeClicks
{
    [LoggerMessage(EventId = 803, Level = LogLevel.Warning, Message = "A click on a notice can't open ModShelf, so it only closes the notice")]
    private static partial void LogCantOpen(ILogger logger, Exception exception);
}
