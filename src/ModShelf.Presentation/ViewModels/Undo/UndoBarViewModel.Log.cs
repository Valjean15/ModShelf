using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Undo;

/// <summary>
/// The undo bar's log messages.
/// </summary>
public sealed partial class UndoBarViewModel
{
    [LoggerMessage(EventId = 957, Level = LogLevel.Error, Message = "Couldn't undo a change to mod {ModId} of {Game}")]
    private static partial void LogUndoFailed(ILogger logger, Exception exception, string modId, string game);
}
