using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// A game picture's log messages.
/// </summary>
public sealed partial class GamePictureViewModel
{
    [LoggerMessage(EventId = 1574, Level = LogLevel.Error, Message = "A game's picture couldn't be shown")]
    private static partial void LogShowFailed(ILogger logger, Exception exception);
}
