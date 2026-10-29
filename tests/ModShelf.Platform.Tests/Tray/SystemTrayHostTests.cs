using System.Runtime.Versioning;

using ModShelf.Platform.Tray;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Tray;

/// <summary>
/// Whether the system shows tray icons: always on Windows and macOS; on Linux, while a tray host runs on the session
/// bus, asked through GIO. The bus is faked for the decisions, and asked for real when the tests run on one.
/// </summary>
public sealed class SystemTrayHostTests
{
    private readonly RecordingLogger<SystemTrayHost> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static bool OnASessionBus => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));

    [Fact]
    public async Task WindowsAndMacOSAlwaysHaveATray()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "Only Windows and macOS always have a tray.");

        SystemTrayHost tray = new(() => throw new InvalidOperationException("The bus isn't asked here."), _logger);

        Assert.True(await tray.IsAvailableAsync(Cancellation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnLinuxItsWhetherATrayHostRuns(bool running)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Tray hosts on the session bus are Linux's.");
        int asked = 0;
        SystemTrayHost tray = new(
            () =>
            {
                asked++;
                return running;
            },
            _logger);

        bool first = await tray.IsAvailableAsync(Cancellation);
        bool second = await tray.IsAvailableAsync(Cancellation);

        Assert.Equal((running, running), (first, second));
        Assert.Equal(2, asked);
        Assert.Equal(
            running ? 0 : 2,
            _logger.Entries.Count(entry => entry.StartsWith("Information: No tray host runs on the session bus", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("bus")]
    [InlineData("gio")]
    public async Task OnLinuxABusThatCantBeAskedMeansNoTray(string problem)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Tray hosts on the session bus are Linux's.");
        SystemTrayHost tray = new(
            () => throw (problem == "bus"
                ? new InvalidOperationException("Cannot autolaunch D-Bus without X11 $DISPLAY")
                : new DllNotFoundException("libgio-2.0.so.0")),
            _logger);

        Assert.False(await tray.IsAvailableAsync(Cancellation));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Couldn't ask the session bus for a tray host, so there's taken to be none: ", StringComparison.Ordinal));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GioAsksTheSessionBusWhoOwnsAName()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && OnASessionBus, "Needs a D-Bus session bus, as CI runs the Linux tests on.");

        // The bus owns its own name; nothing owns a made-up one.
        Assert.True(StatusNotifierInterop.NameHasOwner("org.freedesktop.DBus", TimeSpan.FromSeconds(2)));
        Assert.False(StatusNotifierInterop.NameHasOwner("org.example.ModShelf.NoSuchName", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void WithoutASessionBusGioSaysWhy()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && !OnASessionBus && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            "Needs Linux with neither a session bus nor a display to start one from.");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => StatusNotifierInterop.NameHasOwner(StatusNotifierInterop.WatcherName, TimeSpan.FromSeconds(2)));

        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }
}
