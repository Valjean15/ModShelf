using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Records each notice shown, oldest first.
/// </summary>
internal sealed class RecordingNotices : INotificationService
{
    public List<Notification> Shown { get; } = [];

    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        Shown.Add(notification);
        return Task.CompletedTask;
    }
}
