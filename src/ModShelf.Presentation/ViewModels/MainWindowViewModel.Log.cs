using Microsoft.Extensions.Logging;

using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The main window's log messages.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [LoggerMessage(EventId = 900, Level = LogLevel.Error, Message = "The {Screen} screen's work stopped")]
    private static partial void LogScreenFailed(ILogger logger, Exception exception, Screen screen);

    [LoggerMessage(EventId = 954, Level = LogLevel.Warning, Message = "Couldn't remember the window, the sidebar or the screen last open")]
    private static partial void LogNotRemembered(ILogger logger, Exception exception);
}
