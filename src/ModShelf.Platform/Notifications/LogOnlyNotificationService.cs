using Microsoft.Extensions.Logging;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Logs notices, for a system ModShelf can't show them on.
/// </summary>
internal sealed class LogOnlyNotificationService(ILogger logger) : INotificationService
{
    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        NotificationServices.LogNotice(logger, notification.Title, notification.Message);
        return Task.CompletedTask;
    }
}
