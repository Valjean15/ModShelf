using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The source's log messages.
/// </summary>
public sealed partial class GameBananaSource
{
    [LoggerMessage(EventId = 1800, Level = LogLevel.Information, Message = "Read GameBanana's list of games: {Count} games in {Pages} pages")]
    private static partial void LogGamesRead(ILogger logger, int count, int pages);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Warning, Message = "GameBanana's mod {ModId} of game {GameId} is listed under game {ListedGameId}; it's left out of the search")]
    private static partial void LogModOfAnotherGame(ILogger logger, long modId, string gameId, long listedGameId);
}
