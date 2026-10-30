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
}
