using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// The log messages of reading DEG Mods' games database.
/// </summary>
internal sealed partial class GameCovers
{
    [LoggerMessage(EventId = 2030, Level = LogLevel.Information, Message = "DEG Mods' games database has covers for {Covers} of its {Games} games")]
    private static partial void LogRead(ILogger logger, int covers, int games);

    [LoggerMessage(EventId = 2031, Level = LogLevel.Warning, Message = "DEG Mods' games database couldn't be read, so its games show their initials")]
    private static partial void LogDatabaseUnread(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2032, Level = LogLevel.Warning, Message = "DEG Mods' relays hold no games database, so its games show their initials")]
    private static partial void LogNoDatabase(ILogger logger);

    [LoggerMessage(EventId = 2033, Level = LogLevel.Warning, Message = "No file server gave DEG Mods' games database file {Hash}…, so its games are passed over")]
    private static partial void LogFileUnread(ILogger logger, string hash);

    [LoggerMessage(EventId = 2034, Level = LogLevel.Debug, Message = "The file server {Host} couldn't be asked for a file of DEG Mods' games database")]
    private static partial void LogServerFailed(ILogger logger, Exception exception, string host);
}
