using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Downloads;

/// <summary>
/// The log messages of watching the browser's downloads folder. The folder's path is never logged: it can hold the
/// user's name.
/// </summary>
internal sealed partial class BrowserFolderWatcher
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Debug, Message = "Watching the browser's downloads folder")]
    private static partial void LogWatching(ILogger logger);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Watching the browser's downloads folder failed; files saved meanwhile may be missed")]
    private static partial void LogWatchFailed(ILogger logger, Exception exception);
}
