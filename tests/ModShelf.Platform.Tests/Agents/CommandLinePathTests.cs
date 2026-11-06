using ModShelf.Platform.Agents;

namespace ModShelf.Platform.Tests.Agents;

/// <summary>
/// Where the command line is: as configured, beside the desktop app, or in its own build folder beside the app's.
/// </summary>
public sealed class CommandLinePathTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));

    private static string App => Path.Combine(Root, "src", "ModShelf.Desktop", "bin", "Debug", "net10.0");

    private static string Built => Path.Combine(Root, "src", "ModShelf.Cli", "bin", "Debug", "net10.0", CommandLinePath.ExecutableName);

    [Fact]
    public void BesideTheAppAsInstalled()
    {
        string beside = Path.Combine(App, CommandLinePath.ExecutableName);

        Assert.Equal(beside, CommandLinePath.Find(null, App, path => path == beside || path == Built));
    }

    [Fact]
    public void InItsOwnBuildFolderBesideTheApps()
    {
        Assert.Equal(Built, CommandLinePath.Find(null, App + Path.DirectorySeparatorChar, path => path == Built));
    }

    [Fact]
    public void AsConfiguredWhenItsThere()
    {
        string configured = Path.Combine(Root, "tools", CommandLinePath.ExecutableName);

        Assert.Equal(configured, CommandLinePath.Find(configured, App, path => path == configured || path == Built));
        Assert.Null(CommandLinePath.Find(configured, App, path => path == Built));
    }

    [Fact]
    public void NoneElsewhere()
    {
        string installed = Path.Combine(Root, "ModShelf.App", "current");

        Assert.Null(CommandLinePath.Find(null, installed, _ => false));
        Assert.Null(CommandLinePath.Find(null, App, path => path == Path.Combine(Root, "src", "ModShelf.Cli", "bin", "Release", "net10.0", CommandLinePath.ExecutableName)));
    }

    [Fact]
    public void ItsNameOnThisSystem()
        => Assert.Equal(OperatingSystem.IsWindows() ? "ModShelf.Cli.exe" : "ModShelf.Cli", CommandLinePath.ExecutableName);
}
