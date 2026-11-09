using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// The mod details repository's log messages.
/// </summary>
internal sealed partial class ModDetailsRepository
{
    [LoggerMessage(EventId = 1560, Level = LogLevel.Warning, Message = "The saved details of mod {ModId} of {Game} on {Source} couldn't be read from the library database: {Problem}")]
    private static partial void LogCantFind(ILogger logger, string modId, string game, string source, string problem);

    [LoggerMessage(EventId = 1561, Level = LogLevel.Warning, Message = "The details of mod {ModId} of {Game} on {Source} couldn't be saved in the library database: {Problem}")]
    private static partial void LogCantSave(ILogger logger, string modId, string game, string source, string problem);

    [LoggerMessage(EventId = 1562, Level = LogLevel.Warning, Message = "The saved details of {Count} mods couldn't be read from the library database: {Problem}")]
    private static partial void LogCantFindMany(ILogger logger, int count, string problem);

    [LoggerMessage(EventId = 1563, Level = LogLevel.Warning, Message = "The saved details of mod {ModId} of {Game} on {Source} can't be read, so they're left out: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string modId, string game, string source, string reason);
}
