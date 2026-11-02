using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// A mod picture's log messages.
/// </summary>
public sealed partial class ModPictureViewModel
{
    [LoggerMessage(EventId = 1572, Level = LogLevel.Error, Message = "A mod's picture couldn't be shown")]
    private static partial void LogShowFailed(ILogger logger, Exception exception);
}
