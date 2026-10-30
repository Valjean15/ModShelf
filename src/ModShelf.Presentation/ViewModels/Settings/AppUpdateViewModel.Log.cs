using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// ModShelf's own updates' log messages.
/// </summary>
public sealed partial class AppUpdateViewModel
{
    [LoggerMessage(EventId = 935, Level = LogLevel.Information, Message = "ModShelf couldn't look for a newer version as it started")]
    private static partial void LogStartCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 936, Level = LogLevel.Warning, Message = "Looking for a newer ModShelf failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 937, Level = LogLevel.Warning, Message = "Downloading ModShelf {Version} failed")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception, string version);

    [LoggerMessage(EventId = 938, Level = LogLevel.Information, Message = "ModShelf {Version} is downloaded, waiting for a restart")]
    private static partial void LogDownloaded(ILogger logger, string version);

    [LoggerMessage(EventId = 939, Level = LogLevel.Information, Message = "Restarting to update to ModShelf {Version}")]
    private static partial void LogRestarting(ILogger logger, string version);

    [LoggerMessage(EventId = 940, Level = LogLevel.Error, Message = "Restarting to update to ModShelf {Version} failed")]
    private static partial void LogRestartFailed(ILogger logger, Exception exception, string version);
}
