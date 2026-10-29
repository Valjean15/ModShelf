using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The tray's log messages.
/// </summary>
public sealed partial class TrayViewModel
{
    [LoggerMessage(EventId = 923, Level = LogLevel.Warning, Message = "Couldn't read or keep what closing the window does, so it's asked")]
    private static partial void LogCantRemember(ILogger logger, Exception exception);
}
