using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// Freeing up space's log messages.
/// </summary>
internal sealed partial class LibraryCleanup
{
    [LoggerMessage(EventId = 2700, Level = LogLevel.Information, Message = "Listed what could go from the library: {CanGo} files of {Bytes} bytes, and {Kept} kept, asking about {Asked} mods")]
    private static partial void LogPlanned(ILogger logger, int canGo, long bytes, int kept, int asked);

    [LoggerMessage(EventId = 2701, Level = LogLevel.Warning, Message = "Couldn't ask the site which files of mod {ModId} of {Game} it offers")]
    private static partial void LogCantAsk(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 2702, Level = LogLevel.Warning, Message = "Couldn't list {Folder} for the files nothing matches")]
    private static partial void LogCantList(ILogger logger, Exception exception, string folder);

    [LoggerMessage(EventId = 2703, Level = LogLevel.Information, Message = "Deleted {FileName}, which matched no file of mod {ModId} of {Game}, from disk")]
    private static partial void LogDeletedUnmatched(ILogger logger, string fileName, string modId, string game);

    [LoggerMessage(EventId = 2704, Level = LogLevel.Warning, Message = "Couldn't delete {FileName} to free up space")]
    private static partial void LogCantDelete(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 2705, Level = LogLevel.Information, Message = "Freed up {Bytes} bytes, deleting {Deleted} files, and left {Failed}")]
    private static partial void LogDeleted(ILogger logger, int deleted, long bytes, int failed);

    [LoggerMessage(EventId = 2706, Level = LogLevel.Information, Message = "Mod {ModId} of {Game} is gone from its site, so its files are kept")]
    private static partial void LogGone(ILogger logger, string modId, string game);
}
