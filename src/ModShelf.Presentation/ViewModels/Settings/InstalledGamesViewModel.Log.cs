using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Settings;

public sealed partial class InstalledGamesViewModel
{
    [LoggerMessage(EventId = 973, Level = LogLevel.Error, Message = "Reading the games of {Source} failed, so the installed games match its games only as chosen")]
    private static partial void LogGamesUnread(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 974, Level = LogLevel.Error, Message = "Reading the games installed on this computer failed")]
    private static partial void LogInstalledUnread(ILogger logger, Exception exception);
}
