using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// A library mod's log messages, logged as the Library screen's.
/// </summary>
public sealed partial class LibraryModViewModel
{
    [LoggerMessage(EventId = 905, Level = LogLevel.Error, Message = "{Action} of mod {ModId} of {Game} failed")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, ModAction action, string modId, string game);
}
