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
}
