using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The download speed limit's log messages.
/// </summary>
public sealed partial class DownloadSpeedLimitViewModel
{
    [LoggerMessage(EventId = 989, Level = LogLevel.Warning, Message = "Couldn't save the download speed limit")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 990, Level = LogLevel.Warning, Message = "Couldn't read the download speed limit")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);
}
