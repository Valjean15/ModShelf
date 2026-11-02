using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The log messages of files queued for the library at once. The link itself is never logged, since it can carry a key.
/// </summary>
internal static partial class LibraryBatch
{
    [LoggerMessage(EventId = 944, Level = LogLevel.Error, Message = "Queuing a file of mod {ModId} of {Game} for the library failed")]
    private static partial void LogQueueFailed(ILogger logger, Exception exception, string modId, string game);
}
