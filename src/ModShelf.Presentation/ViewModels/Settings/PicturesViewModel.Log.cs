using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The pictures settings' log messages.
/// </summary>
public sealed partial class PicturesViewModel
{
    [LoggerMessage(EventId = 1573, Level = LogLevel.Error, Message = "A picture or mod details setting couldn't be saved")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
