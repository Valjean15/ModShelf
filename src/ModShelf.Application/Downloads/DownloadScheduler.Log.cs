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
}
