using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// Where the system finds the desktop app to start with a link: beside this app, or the AppImage it runs from.
/// </summary>
public sealed class DesktopAppPathTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string AppDir => Path.Combine(_folder.Path, ".mount_ModShelf");

    private string AppImage => Path.Combine(_folder.Path, "ModShelf.AppImage");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void IsBesideThisAppOutsideAnAppImage()
    {
        string folder = Path.Combine(_folder.Path, "app");

        Assert.Equal(Beside(folder), DesktopAppPath.Of(folder, null, null));
    }

    [Fact]
    public void IsTheAppImageThisAppRunsFromOnLinux()
    {
        File.WriteAllText(AppImage, "an AppImage");
        string folder = Path.Combine(AppDir, "usr", "bin");

        string app = DesktopAppPath.Of(folder, AppImage, AppDir + Path.DirectorySeparatorChar);

        Assert.Equal(OperatingSystem.IsLinux() ? AppImage : Beside(folder), app);
    }

    [Fact]
    public void AnotherAppsAppImageIsntThisOnes()
    {
        // Such as a terminal's AppImage, whose variables what it starts inherits.
        File.WriteAllText(AppImage, "a terminal's AppImage");
        string folder = Path.Combine(_folder.Path, "app");
        string alike = AppDir + "2";

        Assert.Equal(Beside(folder), DesktopAppPath.Of(folder, AppImage, AppDir));
        Assert.Equal(Beside(alike), DesktopAppPath.Of(alike, AppImage, AppDir));
    }

    [Fact]
    public void AnAppImageThatIsntThereIsntUsed()
    {
        string folder = Path.Combine(AppDir, "usr", "bin");

        Assert.Equal(Beside(folder), DesktopAppPath.Of(folder, AppImage, AppDir));
    }

    private static string Beside(string folder) => Path.Combine(folder, OperatingSystem.IsWindows() ? "ModShelf.Desktop.exe" : "ModShelf.Desktop");
}
