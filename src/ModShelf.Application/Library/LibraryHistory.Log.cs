using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The library history's log messages.
/// </summary>
internal sealed partial class LibraryHistory
{
    [LoggerMessage(EventId = 626, Level = LogLevel.Warning, Message = "Couldn't keep {EventCount} of the library's events in its history")]
    private static partial void LogCantRecord(ILogger logger, Exception exception, int eventCount);
}
