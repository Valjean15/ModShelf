using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The Downloads screen's log messages. The link itself is never logged, since it can carry a key.
/// </summary>
public sealed partial class DownloadsViewModel
{
    [LoggerMessage(EventId = 901, Level = LogLevel.Error, Message = "Adding a download failed")]
    private static partial void LogAddFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 903, Level = LogLevel.Error, Message = "Clearing the finished downloads failed")]
    private static partial void LogClearFailed(ILogger logger, Exception exception);
}
