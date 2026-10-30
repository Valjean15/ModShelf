using ModShelf.Platform.Links;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// The Exec key of ModShelf's desktop entry, quoted as the Desktop Entry specification says.
/// </summary>
public sealed class DesktopEntryExecTests
{
    [Fact]
    public void LeavesPlainArgumentsAsTheyAreThenTakesTheLink()
    {
        // As xdg-open reads them outside a desktop environment, splitting at spaces and keeping quotes.
        Assert.Equal(
            "/opt/ModShelf/ModShelf.Desktop --ModShelf:DatabasePath=/tmp/test/library.db --ModShelf:SecretScope=ModShelf.Checklist %u",
            DesktopEntryExec.ForLink("/opt/ModShelf/ModShelf.Desktop", ["--ModShelf:DatabasePath=/tmp/test/library.db", "--ModShelf:SecretScope=ModShelf.Checklist"]));
    }

    [Fact]
    public void QuotesAnArgumentWithASpace()
    {
        Assert.Equal(
            "\"/opt/Mod Shelf/ModShelf.Desktop\" --ModShelf:DatabasePath=/tmp/test/library.db \"\" %u",
            DesktopEntryExec.ForLink("/opt/Mod Shelf/ModShelf.Desktop", ["--ModShelf:DatabasePath=/tmp/test/library.db", string.Empty]));
    }

    [Theory]
    [InlineData("a\"b", "\"a\\\\\"b\"")]
    [InlineData("$HOME", "\"\\\\$HOME\"")]
    [InlineData("`x`", "\"\\\\`x\\\\`\"")]
    [InlineData("back\\slash", "\"back\\\\\\\\slash\"")]
    [InlineData("100% done", "\"100%% done\"")]
    [InlineData("100%", "100%%")]
    public void EscapesWhatTheSpecificationReserves(string argument, string quoted)
    {
        Assert.Equal($"{quoted} %u", DesktopEntryExec.ForLink(argument, []));
    }
}
