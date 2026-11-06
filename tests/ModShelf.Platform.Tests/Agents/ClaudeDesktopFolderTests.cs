using ModShelf.Platform.Agents;

namespace ModShelf.Platform.Tests.Agents;

/// <summary>
/// Where Claude Desktop keeps its configuration on Windows: in roaming application data, or, installed from the
/// Microsoft Store or another MSIX installer, in its package's copy of it.
/// </summary>
public sealed class ClaudeDesktopFolderTests : IDisposable
{
    private readonly ModShelf.Testing.TempFolder _folder = new();

    private string Roaming => Path.Combine(_folder.Path, "Roaming");

    private string Local => Path.Combine(_folder.Path, "Local");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void RoamingApplicationDataByDefault()
    {
        Directory.CreateDirectory(Path.Combine(Local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe"));

        Assert.Equal(Path.Combine(Roaming, "Claude"), ClaudeDesktopFolder.OnWindows(Roaming, Local, Directory.Exists));
        Assert.Equal(Path.Combine(Roaming, "Claude"), ClaudeDesktopFolder.OnWindows(Roaming, Path.Combine(_folder.Path, "Nowhere"), Directory.Exists));
    }

    [Fact]
    public void ThePackagesCopyForTheStoresApp()
    {
        Directory.CreateDirectory(Path.Combine(Local, "Packages", "Claude_pzs8sxrjxfjjc"));

        Assert.Equal(
            Path.Combine(Local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude"),
            ClaudeDesktopFolder.OnWindows(Roaming, Local, Directory.Exists));
    }

    [Fact]
    public void NoneWithoutApplicationData() => Assert.Null(ClaudeDesktopFolder.OnWindows(string.Empty, string.Empty, Directory.Exists));
}
