using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The Updates screen's log messages.
/// </summary>
public sealed partial class UpdatesViewModel
{
    [LoggerMessage(EventId = 907, Level = LogLevel.Error, Message = "Checking the watched mods for updates failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
