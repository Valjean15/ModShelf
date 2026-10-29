using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The Settings screen's log messages.
/// </summary>
public sealed partial class SettingsViewModel
{
    [LoggerMessage(EventId = 921, Level = LogLevel.Error, Message = "Changing the settings failed")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 922, Level = LogLevel.Error, Message = "Changing the library's folder failed")]
    private static partial void LogMoveFailed(ILogger logger, Exception exception);
}
