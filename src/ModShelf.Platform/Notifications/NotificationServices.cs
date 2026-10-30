using Microsoft.Extensions.Logging;

using ModShelf.Application.Links.Ports;
using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Picks how notices are shown on the system the app runs on.
/// </summary>
internal static partial class NotificationServices
{
    /// <summary>
    /// The name notices go by where the system shows one, such as the header of a Windows toast.
    /// </summary>
    public const string AppName = "ModShelf";

    /// <summary>
    /// Toasts on Windows, <c>osascript</c> on macOS, and libnotify on Linux when it's installed. Otherwise notices are
    /// only logged, which is logged as a warning.
    /// </summary>
    /// <param name="links">The system's register of link schemes, where a click on a Windows toast finds ModShelf.</param>
    public static INotificationService ForThisSystem(ISchemeRegistration links, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(logger);

        if (OperatingSystem.IsWindows())
        {
            return new ToastNotificationService(new NoticeClicks(links, logger), logger);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new OsascriptNotificationService(logger);
        }

        if (OperatingSystem.IsLinux() && LibNotifyInterop.IsAvailable())
        {
            return new LibNotifyNotificationService(logger);
        }

        LogNoNotices(logger);
        return new LogOnlyNotificationService(logger);
    }
}
