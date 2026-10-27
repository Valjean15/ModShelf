using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

public sealed class NotificationServicesTests
{
    /// <summary>
    /// Set to 1 to see a real notice from <see cref="ShowsARealNotice"/>, which is skipped otherwise.
    /// </summary>
    private const string ShowNoticesVariable = "MODSHELF_TEST_NOTICES";

    private readonly RecordingLogger<NotificationServicesTests> _logger = new();

    [Fact]
    public void PicksTheNoticesOfThisSystem()
    {
        INotificationService notices = NotificationServices.ForThisSystem(_logger);

        Type expected = OperatingSystem.IsWindows() ? typeof(ToastNotificationService)
            : OperatingSystem.IsMacOS() ? typeof(OsascriptNotificationService)
            : OperatingSystem.IsLinux() && LibNotifyInterop.IsAvailable() ? typeof(LibNotifyNotificationService)
            : typeof(LogOnlyNotificationService);
        Assert.IsType(expected, notices);
        Assert.Equal(expected == typeof(LogOnlyNotificationService), _logger.Entries.Any(entry => entry.StartsWith("Warning: This system has no way", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Shows a real notice, for a person to see: <c>MODSHELF_TEST_NOTICES=1</c>, then run this test.
    /// </summary>
    [Fact]
    public async Task ShowsARealNotice()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(ShowNoticesVariable) == "1", $"Set {ShowNoticesVariable}=1 to see a real notice.");

        await NotificationServices.ForThisSystem(_logger)
            .ShowAsync(new Notification("1 new update", "If you can see this, ModShelf's notices work."), TestContext.Current.CancellationToken);

        Assert.Empty(_logger.Entries);
    }
}
