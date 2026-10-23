using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database's log messages.
/// </summary>
internal static partial class LibraryDatabaseLog
{
    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Updating the library database: applying {Count} migration(s), ending with {LastMigration}")]
    public static partial void ApplyingMigrations(this ILogger logger, int count, string lastMigration);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug, Message = "The library database is up to date")]
    public static partial void UpToDate(this ILogger logger);
}
