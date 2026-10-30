using ModShelf.Platform.Links;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// Quoting arguments the way Windows programs read them back, and naming the app a registered command starts.
/// </summary>
public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData(@"C:\Program Files\ModShelf\ModShelf.Desktop.exe", @"""C:\Program Files\ModShelf\ModShelf.Desktop.exe""")]
    [InlineData("plain", @"""plain""")]
    [InlineData(@"C:\Ends in\", @"""C:\Ends in\\""")]
    [InlineData(@"say ""hi""", @"""say \""hi\""""")]
    [InlineData(@"a\""b", @"""a\\\""b""")]
    [InlineData("", @"""""")]
    public void QuotesSoTheArgumentReadsBackAsItIs(string argument, string quoted)
    {
        Assert.Equal(quoted, WindowsCommandLine.Quote(argument));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Vortex\Vortex.exe"" -d ""%1""", @"C:\Program Files\Vortex\Vortex.exe")]
    [InlineData(@"C:\Program Files\Vortex\Vortex.exe -d ""%1""", @"C:\Program Files\Vortex\Vortex.exe")]
    [InlineData(@"C:\MO2\nxmhandler ""%1""", @"C:\MO2\nxmhandler")]
    [InlineData("  handler  ", "handler")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData(@"""""", null)]
    public void FindsTheProgramACommandStarts(string? command, string? program)
    {
        Assert.Equal(program, WindowsCommandLine.Program(command));
    }

    [Theory]
    [InlineData(@"""C:\Programs\Vortex\Vortex.exe"" -d ""%1""", "Vortex")]
    [InlineData(@"C:\MO2\nxmhandler.exe ""%1""", "nxmhandler")]
    [InlineData("handler", "handler")]
    public void NamesAMissingProgramByItsFile(string command, string name)
    {
        Assert.Equal(name, WindowsCommandLine.AppName(command));
    }

    [Fact]
    public void NamesAProgramThatIsThereByItsDescription()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows programs carry a description in their version information.");
        string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.SkipUnless(File.Exists(notepad), "This Windows has no Notepad.");

        Assert.Equal(
            System.Diagnostics.FileVersionInfo.GetVersionInfo(notepad).FileDescription,
            WindowsCommandLine.AppName($"\"{notepad}\" \"%1\""));
    }
}
