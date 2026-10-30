using Microsoft.Extensions.Logging;

using ModShelf.Domain.Downloads;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// A download's log messages, logged as the Downloads screen's.
/// </summary>
public sealed partial class DownloadJobViewModel
{
    [LoggerMessage(EventId = 902, Level = LogLevel.Error, Message = "{Action} of download job {JobId} failed")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, JobAction action, DownloadJobId jobId);
}
