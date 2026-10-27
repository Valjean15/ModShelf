using System.Runtime.Versioning;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

/// <summary>
/// Runs on Linux with libnotify. Whether a notice shows depends on the desktop's notification server, which CI doesn't
/// run, so a notice either shows or is logged, and never throws.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LibNotifyNotificationServiceTests
{
    private readonly RecordingLogger<LibNotifyNotificationServiceTests> _logger = new();

    [Fact]
    public async Task ShowsANoticeOrLogsWhyNot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && LibNotifyInterop.IsAvailable(), "libnotify is a Linux library, and isn't installed here.");

        await new LibNotifyNotificationService(_logger).ShowAsync(new Notification("1 new update", "A test of ModShelf's notices."), TestContext.Current.CancellationToken);

        Assert.All(_logger.Entries, entry => Assert.StartsWith("Warning: Couldn't show the notice \"1 new update\"", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutASessionBusTheNoticeIsLogged()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && LibNotifyInterop.IsAvailable(), "libnotify is a Linux library, and isn't installed here.");
        Assert.SkipUnless(!HasSessionBus(), "A session bus is running here, so the notice may show.");

        await new LibNotifyNotificationService(_logger).ShowAsync(new Notification("1 new update", "A test of ModShelf's notices."), TestContext.Current.CancellationToken);

        Assert.StartsWith("Warning: Couldn't show the notice \"1 new update\"", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether D-Bus clients find a session bus here: through its address, or the user's bus socket systemd makes.
    /// </summary>
    private static bool HasSessionBus()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            || (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && File.Exists(Path.Combine(runtime, "bus")));
}
