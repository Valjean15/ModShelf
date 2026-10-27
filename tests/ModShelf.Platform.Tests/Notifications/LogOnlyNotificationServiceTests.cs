using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

public sealed class LogOnlyNotificationServiceTests
{
    [Fact]
    public async Task LogsTheNotice()
    {
        RecordingLogger<LogOnlyNotificationServiceTests> logger = new();

        await new LogOnlyNotificationService(logger).ShowAsync(new Notification("1 new update", "SkyUI"), TestContext.Current.CancellationToken);

        Assert.Equal("Information: Notice: 1 new update: SkyUI ", Assert.Single(logger.Entries));
    }
}
