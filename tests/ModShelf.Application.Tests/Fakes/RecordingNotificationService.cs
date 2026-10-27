using System.Collections.Concurrent;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the notices it's asked to show, or fails as the test says.
/// </summary>
internal sealed class RecordingNotificationService : INotificationService
{
    private readonly ConcurrentQueue<Notification> _shown = new();

    public IReadOnlyList<Notification> Shown => [.. _shown];

    public Exception? Failure { get; set; }

    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        _shown.Enqueue(notification);
        return Task.CompletedTask;
    }
}
