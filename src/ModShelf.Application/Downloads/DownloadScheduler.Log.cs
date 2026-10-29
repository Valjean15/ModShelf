using Microsoft.Extensions.Logging;

using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The scheduler's log messages. They name jobs and sources only: never URLs, tickets or headers.
/// </summary>
internal sealed partial class DownloadScheduler
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Requeued {Count} download jobs interrupted when the app last closed")]
    private static partial void LogRequeuedInterruptedJobs(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Download job {JobId} failed: {Reason}")]
    private static partial void LogJobFailed(ILogger logger, DownloadJobId jobId, DownloadFailureReason reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Download job {JobId} needs source {SourceId}, which isn't registered or can't resolve downloads")]
    private static partial void LogNoResolver(ILogger logger, DownloadJobId jobId, SourceId sourceId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Download job {JobId} failed unexpectedly")]
    private static partial void LogAttemptCrashed(ILogger logger, Exception exception, DownloadJobId jobId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Couldn't apply an update to download job {JobId}")]
    private static partial void LogUpdateRejected(ILogger logger, Exception exception, DownloadJobId jobId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Source {SourceId} couldn't resolve download job {JobId}")]
    private static partial void LogResolveFailed(ILogger logger, Exception exception, DownloadJobId jobId, SourceId sourceId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Cleared {Count} finished download jobs from the queue")]
    private static partial void LogClearedFinished(ILogger logger, int count);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Couldn't save download job {JobId}; it's saved again once the database can be written")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, DownloadJobId jobId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Another ModShelf process runs the downloads of this library; they start here once it lets go")]
    private static partial void LogQueueBusyElsewhere(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "The other ModShelf process let go of the downloads, so they run here now")]
    private static partial void LogQueueRunsHere(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Couldn't read the download queue from the library database; trying again in {Delay}")]
    private static partial void LogCantLoadJobs(ILogger logger, Exception exception, TimeSpan delay);
}
