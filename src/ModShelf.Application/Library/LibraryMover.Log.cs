using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Library;

/// <summary>
/// The library mover's log messages.
/// </summary>
internal sealed partial class LibraryMover
{
    [LoggerMessage(EventId = 610, Level = LogLevel.Information, Message = "Moved the library from {From} to {To}: {Count} files")]
    private static partial void LogMoved(ILogger logger, string from, string to, int count);

    [LoggerMessage(EventId = 611, Level = LogLevel.Warning, Message = "Couldn't move {Path}, so the {Count} files moved before it went back")]
    private static partial void LogFailed(ILogger logger, Exception exception, string path, int count);

    [LoggerMessage(EventId = 612, Level = LogLevel.Error, Message = "Couldn't move {Path} back while undoing a move of the library")]
    private static partial void LogCantMoveBack(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 615, Level = LogLevel.Warning, Message = "Couldn't save the library's new root {To}, so the {Count} files moved went back")]
    private static partial void LogCantSaveRoot(ILogger logger, Exception exception, string to, int count);

    [LoggerMessage(EventId = 616, Level = LogLevel.Warning, Message = "A download into {From} started while the library moved from it, so the {Count} files moved went back")]
    private static partial void LogDownloadStarted(ILogger logger, string from, int count);
}
