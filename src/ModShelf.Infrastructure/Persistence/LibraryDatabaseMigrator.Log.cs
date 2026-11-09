using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database's log messages.
/// </summary>
internal sealed partial class LibraryDatabaseMigrator
{
    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Updating the library database: applying {Count} migration(s), ending with {LastMigration}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, string lastMigration);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug, Message = "The library database is up to date")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Error, Message = "The library database {Path} can't be opened")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "Cleared the lock an update of the library database left when it stopped part-way")]
    private static partial void LogClearedStoppedUpdate(ILogger logger);

    [LoggerMessage(EventId = 207, Level = LogLevel.Information, Message = "The library database now writes ahead of its file (WAL), so reading it never waits for a change")]
    private static partial void LogWritingAhead(ILogger logger);

    [LoggerMessage(EventId = 208, Level = LogLevel.Warning, Message = "The library database can't write ahead of its file (WAL) where it is, so it keeps its {Mode} journal, and reading it can wait for a change")]
    private static partial void LogCantWriteAhead(ILogger logger, string mode);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning, Message = "The library database couldn't start writing ahead of its file (WAL) while another program used it; the next start tries again")]
    private static partial void LogWriteAheadLater(ILogger logger, Exception exception);
}
