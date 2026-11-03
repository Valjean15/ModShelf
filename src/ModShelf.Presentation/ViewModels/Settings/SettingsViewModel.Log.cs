using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The Settings screen's log messages.
/// </summary>
public sealed partial class SettingsViewModel
{
    [LoggerMessage(EventId = 921, Level = LogLevel.Error, Message = "Changing the settings failed")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 922, Level = LogLevel.Error, Message = "Changing the library's folder failed")]
    private static partial void LogMoveFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 947, Level = LogLevel.Error, Message = "Rescanning the library from the Settings screen failed")]
    private static partial void LogRescanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 948, Level = LogLevel.Warning, Message = "Couldn't list the library's games for the Settings screen")]
    private static partial void LogGamesUnreadable(ILogger logger, Exception exception);
}
