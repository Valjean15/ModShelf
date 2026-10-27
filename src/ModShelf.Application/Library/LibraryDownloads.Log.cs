using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The library downloads' log messages.
/// </summary>
internal sealed partial class LibraryDownloads
{
    [LoggerMessage(EventId = 600, Level = LogLevel.Information, Message = "Added {FileName} to the library, as a file of mod {ModId} of {Game}")]
    private static partial void LogRecorded(ILogger logger, string fileName, string modId, string game);

    [LoggerMessage(EventId = 601, Level = LogLevel.Warning, Message = "Couldn't add {FileName} to the library")]
    private static partial void LogCantRecord(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 602, Level = LogLevel.Warning, Message = "Couldn't look for finished downloads missing from the library")]
    private static partial void LogCantLookForMissed(ILogger logger, Exception exception);
}
