using ModShelf.Application.Settings;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// The Downloads screen's line for the speed limit, read again each time the screen shows, with its switch (ADR 0105).
/// </summary>
public sealed class DownloadsSpeedLimitTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private readonly DownloadsHarness _harness = new();

    private DownloadsViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task WithoutALimitTheLineIsntShown()
    {
        await _harness.StartWithAsync();

        await ShowAsync();

        Assert.False(Screen.SpeedLimit.HasLimit);
    }

    [Fact]
    public async Task ShowsTheLimitAsTheSettingsHaveItEachTimeTheScreenShows()
    {
        await _harness.StartWithAsync();
        _harness.Settings.Stored = new AppSettings { DownloadSpeedLimit = 2 * Megabyte };
        await ShowAsync();
        string first = Screen.SpeedLimit.StateText;

        // As the command line or an agent would change it.
        _harness.Settings.Stored = new AppSettings { DownloadSpeedLimit = 2 * Megabyte, DownloadSpeedLimitOn = false };
        await ShowAsync();

        Assert.Equal("Limited to 2.00 MB/s", first);
        Assert.Equal((true, false, "Not limited (2.00 MB/s when on)"), (Screen.SpeedLimit.HasLimit, Screen.SpeedLimit.IsLimited, Screen.SpeedLimit.StateText));
        Assert.Empty(_harness.Settings.SpeedLimits);
    }

    [Fact]
    public async Task TheSwitchTurnsTheLimitOffAndOnKeepingItsSpeed()
    {
        await _harness.StartWithAsync();
        _harness.Settings.Stored = new AppSettings { DownloadSpeedLimit = 768 * 1024 };
        await ShowAsync();

        Screen.SpeedLimit.IsLimited = false;
        await Screen.SpeedLimit.Saving;
        string off = Screen.SpeedLimit.StateText;
        Screen.SpeedLimit.IsLimited = true;
        await Screen.SpeedLimit.Saving;

        Assert.Equal(new (long?, bool)[] { (768 * 1024, false), (768 * 1024, true) }, _harness.Settings.SpeedLimits);
        Assert.Equal("Not limited (768 KB/s when on)", off);
        Assert.Equal("Limited to 768 KB/s", Screen.SpeedLimit.StateText);
    }

    /// <summary>
    /// Shows the screen, as the main window does, and runs what it posts.
    /// </summary>
    private async Task ShowAsync()
    {
        Screen.OnShown();
        await Screen.SpeedLimitWork;
        _harness.UiThread.RunPending();
    }
}
