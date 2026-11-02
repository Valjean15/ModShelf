using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// The Library screen's log messages.
/// </summary>
public sealed partial class LibraryViewModel
{
    [LoggerMessage(EventId = 941, Level = LogLevel.Error, Message = "Rescanning the library failed")]
    private static partial void LogRescanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 942, Level = LogLevel.Error, Message = "Opening mod {ModId} of {Game} failed")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string modId, string game);

    [LoggerMessage(EventId = 943, Level = LogLevel.Warning, Message = "Checking the files and saved details of game {Game}'s mods failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception, string game);
}
