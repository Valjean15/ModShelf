using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Tray;

/// <summary>
/// The tray check's log messages.
/// </summary>
internal sealed partial class SystemTrayHost
{
    [LoggerMessage(
        EventId = 1210,
        Level = LogLevel.Information,
        Message = "No tray host runs on the session bus, so closing the window can't keep ModShelf in the tray. On GNOME, the AppIndicator extension adds one.")]
    private static partial void LogNoTrayHost(ILogger logger);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Information, Message = "Couldn't ask the session bus for a tray host, so there's taken to be none: {Problem}")]
    private static partial void LogCantAsk(ILogger logger, string? problem);
}
