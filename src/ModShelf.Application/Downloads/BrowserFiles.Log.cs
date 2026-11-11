using Microsoft.Extensions.Logging;

using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The log messages of watching the browser's downloads folder. A file is named by its name alone, never its path.
/// </summary>
internal sealed partial class BrowserFiles
{
    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Watching the browser's downloads folder for {Count} downloads waiting on a site's page")]
    private static partial void LogWatching(ILogger logger, int count);

    [LoggerMessage(EventId = 31, Level = LogLevel.Warning, Message = "The browser's downloads folder can't be watched")]
    private static partial void LogCantWatch(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 32, Level = LogLevel.Information, Message = "The browser saved {FileName}, for download job {JobId}: {Outcome}")]
    private static partial void LogTook(ILogger logger, string fileName, DownloadJobId jobId, TakeFileOutcome outcome);
}
