using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.Common;

/// <summary>
/// The log messages of reading games' names, logged as the screen's that asked.
/// </summary>
internal static partial class GameNames
{
    [LoggerMessage(EventId = 904, Level = LogLevel.Information, Message = "Couldn't read the name of game {Game} from {Source}, so it goes by its key: {Reason}")]
    private static partial void LogGameNameUnknown(ILogger logger, string game, string source, string reason);

    [LoggerMessage(EventId = 909, Level = LogLevel.Warning, Message = "Reading the name of game {Game} from {Source} failed, so it goes by its key")]
    private static partial void LogGameNameFailed(ILogger logger, Exception exception, string game, string source);
}
