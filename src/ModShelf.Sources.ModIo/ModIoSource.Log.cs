using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The source's log messages.
/// </summary>
public sealed partial class ModIoSource
{
    [LoggerMessage(EventId = 1900, Level = LogLevel.Information, Message = "Read mod.io's list of games open to apps: {Count} games in {Pages} pages")]
    private static partial void LogGamesRead(ILogger logger, int count, int pages);

    [LoggerMessage(EventId = 1902, Level = LogLevel.Warning, Message = "mod.io's virus scan says file {FileId} of mod {ModId} may be harmful, such as one with a program in it; it's downloaded as asked")]
    private static partial void LogMayBeHarmful(ILogger logger, long fileId, long modId);

    [LoggerMessage(EventId = 1903, Level = LogLevel.Warning, Message = "mod.io's virus scan found file {FileId} of mod {ModId} malicious; ModShelf doesn't download it")]
    private static partial void LogMalicious(ILogger logger, long fileId, long modId);

    [LoggerMessage(EventId = 1904, Level = LogLevel.Information, Message = "mod.io lists {Total} file changes of game {GameId} since {Since:O}, more than ModShelf reads at once: its mods are checked one by one")]
    private static partial void LogTooManyChanges(ILogger logger, int total, long gameId, DateTimeOffset since);
}
