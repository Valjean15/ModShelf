using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

/// <summary>
/// Runs on Windows, under an app ID of its own that the tests remove. Nothing here shows a toast: the Windows Runtime
/// calls are checked up to the last one, Show, which <c>NotificationServicesTests</c> makes when asked to.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ToastNotificationServiceTests : IDisposable
{
    private readonly string _applicationId = $"ModShelf.Tests.{Guid.NewGuid():N}";
    private readonly RecordingLogger<ToastNotificationServiceTests> _logger = new();

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(new ToastNotificationService(_applicationId, _logger).RegistryKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RegistersItsAppIdForTheCurrentUserOnce()
    {
        SkipUnlessWindows();
        ToastNotificationService service = new(_applicationId, _logger);

        service.Register();
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(service.RegistryKey, writable: true)!)
        {
            key.SetValue("DisplayName", "Changed while running");
        }

        service.Register();

        using RegistryKey registered = Registry.CurrentUser.OpenSubKey(service.RegistryKey)!;
        Assert.Equal(@"Software\Classes\AppUserModelId\" + _applicationId, service.RegistryKey);
        Assert.Equal("Changed while running", registered.GetValue("DisplayName"));
    }

    [Fact]
    public void PutsModShelfsNameBackOnAnIdRegisteredOtherwise()
    {
        SkipUnlessWindows();
        ToastNotificationService service = new(_applicationId, _logger);
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(service.RegistryKey))
        {
            key.SetValue("DisplayName", "Something else");
        }

        service.Register();

        using RegistryKey registered = Registry.CurrentUser.OpenSubKey(service.RegistryKey)!;
        Assert.Equal("ModShelf", registered.GetValue("DisplayName"));
    }

    [Fact]
    public void MakesAToastAndANotifierWithoutShowingIt()
    {
        SkipUnlessWindows();
        ToastInterop.Initialize();

        IntPtr toast = ToastInterop.CreateToast(ToastContent.For(new Notification("1 new update", "Never shown.")));
        IntPtr notifier = ToastInterop.CreateNotifier(_applicationId);
        try
        {
            Assert.NotEqual(IntPtr.Zero, toast);
            Assert.NotEqual(IntPtr.Zero, notifier);
        }
        finally
        {
            _ = Marshal.Release(toast);
            _ = Marshal.Release(notifier);
        }
    }

    [Fact]
    public void RefusesXmlThatIsntWellFormed()
    {
        SkipUnlessWindows();
        ToastInterop.Initialize();

        Assert.ThrowsAny<ExternalException>(() => ToastInterop.CreateToast("<toast><visual>"));
    }

    private static void SkipUnlessWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts exist only on Windows.");
}
