using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Shows notices on Linux through libnotify, which hands them to the desktop's notification server, such as GNOME
/// Shell's or KDE Plasma's.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LibNotifyNotificationService(ILogger logger) : INotificationService
{
    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // libnotify waits for the notification server's answer.
        return Task.Run(
            () =>
            {
                try
                {
                    Show(notification);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    NotificationServices.LogCantShow(logger, exception, notification.Title);
                }
            },
            cancellationToken);
    }

    private static void Show(Notification notification)
    {
        if (!LibNotifyInterop.Init(NotificationServices.AppName))
        {
            throw new InvalidOperationException("libnotify couldn't start.");
        }

        IntPtr notice = LibNotifyInterop.NotificationNew(notification.Title, notification.Message, null);
        if (notice == IntPtr.Zero)
        {
            throw new InvalidOperationException("libnotify couldn't make the notice.");
        }

        try
        {
            if (!LibNotifyInterop.NotificationShow(notice, out IntPtr error))
            {
                string reason = LibNotifyInterop.TakeErrorMessage(error) ?? "it gave no reason";
                throw new InvalidOperationException($"The desktop's notification server didn't show it: {reason}");
            }
        }
        finally
        {
            LibNotifyInterop.ObjectUnref(notice);
        }
    }
}
