using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// The Browse screen's log messages.
/// </summary>
public sealed partial class BrowseViewModel
{
    [LoggerMessage(EventId = 910, Level = LogLevel.Error, Message = "Reading the games of {Source} failed")]
    private static partial void LogGamesFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 911, Level = LogLevel.Error, Message = "Listing or opening mods failed")]
    private static partial void LogListingFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 945, Level = LogLevel.Error, Message = "Listing the {Row} row failed")]
    private static partial void LogRowFailed(ILogger logger, Exception exception, string row);

    [LoggerMessage(EventId = 946, Level = LogLevel.Warning, Message = "Reading the categories of game {Game} from {Source} failed")]
    private static partial void LogCategoriesFailed(ILogger logger, Exception exception, string game, string source);

    [LoggerMessage(EventId = 951, Level = LogLevel.Warning, Message = "Couldn't remember Browse's search, so the game picker doesn't offer it")]
    private static partial void LogSearchNotRemembered(ILogger logger, Exception exception);
}
