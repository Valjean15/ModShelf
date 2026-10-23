using System.ComponentModel;
using System.Diagnostics;

using ModShelf.Platform.Browser;

namespace ModShelf.Platform.Tests.Browser;

public sealed class SystemBrowserLauncherTests
{
    private const string Page = "https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407";

    private readonly List<ProcessStartInfo> _started = [];

    [Fact]
    public async Task OpensTheLinkWithTheSystemsOpenerWithoutAShell()
    {
        SystemBrowserLauncher launcher = new(Record);

        Assert.True(await launcher.TryOpenAsync(new Uri(Page), TestContext.Current.CancellationToken));

        ProcessStartInfo started = Assert.Single(_started);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(new Uri(Page).AbsoluteUri, started.FileName);
            Assert.True(started.UseShellExecute);
        }
        else
        {
            Assert.Equal(OperatingSystem.IsMacOS() ? "open" : "xdg-open", started.FileName);
            Assert.Equal([new Uri(Page).AbsoluteUri], started.ArgumentList);
            Assert.False(started.UseShellExecute);
            Assert.Empty(started.Arguments);
        }
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("nxm://skyrimspecialedition/mods/12604/files/35407")]
    public async Task RefusesLinksThatAreNotWebPages(string link)
    {
        SystemBrowserLauncher launcher = new(Record);

        await Assert.ThrowsAsync<ArgumentException>(
            () => launcher.TryOpenAsync(new Uri(link), TestContext.Current.CancellationToken));
        Assert.Empty(_started);
    }

    [Fact]
    public async Task ReportsWhenNoBrowserCouldBeStarted()
    {
        SystemBrowserLauncher launcher = new(_ => throw new Win32Exception(2, "No such file or directory"));

        Assert.False(await launcher.TryOpenAsync(new Uri(Page), TestContext.Current.CancellationToken));
    }

    private Process? Record(ProcessStartInfo info)
    {
        _started.Add(info);
        return null;
    }
}
