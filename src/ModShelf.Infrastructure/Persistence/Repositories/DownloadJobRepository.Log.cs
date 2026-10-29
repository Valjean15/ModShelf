using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// The download job repository's log messages.
/// </summary>
internal sealed partial class DownloadJobRepository
{
    [LoggerMessage(EventId = 203, Level = LogLevel.Warning, Message = "Download job {JobId} in the library database can't be loaded, so it's left out of the queue")]
    private static partial void LogJobLeftOut(ILogger logger, Exception exception, Guid jobId);
}
