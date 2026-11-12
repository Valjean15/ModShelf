using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Installed;

internal sealed partial class InstalledGameMatcher
{
    [LoggerMessage(EventId = 2210, Level = LogLevel.Debug, Message = "Matched {Games} installed games, {Matched} of them with a site's game")]
    private static partial void LogMatched(ILogger logger, int games, int matched);

    [LoggerMessage(EventId = 2211, Level = LogLevel.Warning, Message = "Couldn't read the settings, so the installed games match the sites' games by name alone")]
    private static partial void LogPicksUnread(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2212, Level = LogLevel.Warning, Message = "Couldn't ask {Source} for a game picked for an installed game, so its key stands for its name")]
    private static partial void LogPickedUnread(ILogger logger, string source, Exception exception);
}
