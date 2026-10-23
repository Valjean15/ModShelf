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
}
