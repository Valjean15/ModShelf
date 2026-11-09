using System.Diagnostics;
using System.Globalization;

using ModShelf.Platform.Diagnostics;

namespace ModShelf.Platform.Tests.Diagnostics;

/// <summary>
/// The process that started this one, as each system says it. The command line's tests start it as a process of its
/// own and check it names them, on each system.
/// </summary>
public sealed class ParentProcessTests
{
    [Fact]
    public void TheParentIsAnotherProcessThatsRunning()
    {
        int parent = Assert.NotNull(ParentProcess.FindId());

        Assert.NotEqual(Environment.ProcessId, parent);
        using Process process = Process.GetProcessById(parent);
        Assert.Equal(process.ProcessName, ParentProcess.NameOf(parent));
    }

    [Fact]
    public void OnLinuxItsWhatTheSystemSaysOfThisProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux says it in /proc.");

        string line = Assert.Single(File.ReadAllLines("/proc/self/status"), line => line.StartsWith("PPid:", StringComparison.Ordinal));

        Assert.Equal(int.Parse(line["PPid:".Length..].Trim(), CultureInfo.InvariantCulture), ParentProcess.FindId());
    }

    [Fact]
    public void AnEndedProcessHasNoName()
    {
        using Process ended = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "true", OperatingSystem.IsWindows() ? "/c exit" : string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        int id = ended.Id;
        ended.WaitForExit();
        ended.Dispose();

        Assert.Null(ParentProcess.NameOf(id));
        Assert.Equal(Process.GetCurrentProcess().ProcessName, ParentProcess.NameOf(Environment.ProcessId));
    }

    [Theory]
    [InlineData("4242 (ModShelf.Cli) S 4100 4242 4100 0 -1", 4100)]
    [InlineData("4242 (a (strange) name) S 17 1 1", 17)]
    [InlineData("4242 (with spaces) R 1 2 3", 1)]
    [InlineData("4242 (name) S", null)]
    [InlineData("4242 (name) S x", null)]
    [InlineData("no brackets", null)]
    public void LinuxSaysTheParentAfterTheName(string stat, int? parent) => Assert.Equal(parent, ParentProcess.FromProcStat(stat));
}
