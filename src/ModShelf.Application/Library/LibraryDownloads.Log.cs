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

    [LoggerMessage(EventId = 604, Level = LogLevel.Information, Message = "Moved the old version {FileName} to its _archive folder")]
    private static partial void LogArchived(ILogger logger, string fileName);

    [LoggerMessage(EventId = 605, Level = LogLevel.Warning, Message = "Couldn't move the old version {FileName} to its _archive folder")]
    private static partial void LogCantArchive(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 606, Level = LogLevel.Warning, Message = "Didn't archive the old version {FileName}: both names it could have in the _archive folder are taken")]
    private static partial void LogNoArchiveName(ILogger logger, string fileName);

    [LoggerMessage(EventId = 607, Level = LogLevel.Information, Message = "Deleted the old version {FileName}")]
    private static partial void LogDeleted(ILogger logger, string fileName);

    [LoggerMessage(EventId = 608, Level = LogLevel.Warning, Message = "Couldn't delete the old version {FileName}; it stays in the library")]
    private static partial void LogCantDelete(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 609, Level = LogLevel.Error, Message = "Couldn't move {FileName} back from {ArchivePath} after the library refused the change")]
    private static partial void LogCantPutBack(ILogger logger, Exception exception, string fileName, string archivePath);
}
