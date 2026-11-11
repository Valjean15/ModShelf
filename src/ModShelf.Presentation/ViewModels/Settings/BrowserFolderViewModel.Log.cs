using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The log messages of the browser's downloads folder on the Settings screen. The folder's path is never logged.
/// </summary>
public sealed partial class BrowserFolderViewModel
{
    [LoggerMessage(EventId = 969, Level = LogLevel.Warning, Message = "Couldn't save the browser's downloads folder")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
