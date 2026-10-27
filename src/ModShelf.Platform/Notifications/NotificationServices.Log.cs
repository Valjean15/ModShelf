using Microsoft.Extensions.Logging;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// The notices' log messages, which every way of showing them shares.
/// </summary>
internal static partial class NotificationServices
{
    [LoggerMessage(EventId = 800, Level = LogLevel.Warning, Message = "Couldn't show the notice \"{Title}\"")]
    public static partial void LogCantShow(ILogger logger, Exception exception, string title);

    [LoggerMessage(
        EventId = 801,
        Level = LogLevel.Warning,
        Message = "This system has no way ModShelf knows of to show notices, so they're only logged. On Linux, install libnotify (the libnotify4 or libnotify package) to see them.")]
    public static partial void LogNoNotices(ILogger logger);

    [LoggerMessage(EventId = 802, Level = LogLevel.Information, Message = "Notice: {Title}: {Message}")]
    public static partial void LogNotice(ILogger logger, string title, string message);
}
