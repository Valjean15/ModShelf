namespace ModShelf.Application.Notifications.Ports;

/// <summary>
/// Shows notices the way the system shows them, such as a toast on Windows. Platform implements it for each system.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows <paramref name="notification"/>. A notice the system can't show is logged, not thrown: no notice is what the
    /// user asked for, so none should stop what they did ask for.
    /// </summary>
    Task ShowAsync(Notification notification, CancellationToken cancellationToken);
}
