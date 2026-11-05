using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The log message of the dialog that moves a game's files.
/// </summary>
public sealed partial class GameMoveViewModel
{
    [LoggerMessage(EventId = 953, Level = LogLevel.Error, Message = "Moving the files of {Game} failed")]
    private static partial void LogMoveFailed(ILogger logger, Exception exception, string game);
}
