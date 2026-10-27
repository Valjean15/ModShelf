using System.Text;

using ModShelf.Domain.Library;

namespace ModShelf.Domain.Tests.Library;

public sealed class LibraryNamesTests
{
    [Theory]
    [InlineData("SMAPI 4.5.2.zip", "SMAPI 4.5.2.zip")]
    [InlineData("Better: Faster? \"Mods\" <now>|*", "Better_ Faster_ _Mods_ _now___")]
    [InlineData("Texture\\Pack/HD", "Texture_Pack_HD")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("  Trimmed. . ", "Trimmed")]
    [InlineData("...", "_")]
    [InlineData("", "_")]
    [InlineData(" ", "_")]
    [InlineData("Ünïcödé 模组", "Ünïcödé 模组")]
    public void KeepsWhatEverySystemTakesAndReplacesTheRest(string name, string portable)
    {
        Assert.Equal(portable, LibraryNames.Folder(name));
        Assert.Equal(portable, LibraryNames.File(name));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("NUL.txt", "_NUL.txt")]
    [InlineData("COM1", "_COM1")]
    [InlineData("lpt9.zip", "_lpt9.zip")]
    [InlineData("COM¹", "_COM¹")]
    [InlineData("AUX .tar.gz", "_AUX .tar.gz")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    public void PrefixesNamesWindowsKeepsForDevices(string name, string portable)
    {
        Assert.Equal(portable, LibraryNames.File(name));
    }

    [Fact]
    public void ShortensALongFolderName()
    {
        string folder = LibraryNames.Folder(new string('a', 300));

        Assert.Equal(LibraryNames.MaxFolderBytes, Encoding.UTF8.GetByteCount(folder));
    }

    [Fact]
    public void ShortensALongFileNameKeepingItsExtension()
    {
        string file = LibraryNames.File(new string('a', 300) + ".7z");

        Assert.Equal(LibraryNames.MaxFileBytes, Encoding.UTF8.GetByteCount(file));
        Assert.EndsWith("aaa.7z", file, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsBytesNotCharactersAndNeverSplitsOne()
    {
        // Four bytes each in UTF-8, two chars each in UTF-16.
        string file = LibraryNames.File(string.Concat(Enumerable.Repeat("🎮", 100)) + ".zip");

        Assert.True(Encoding.UTF8.GetByteCount(file) <= LibraryNames.MaxFileBytes);
        Assert.EndsWith("🎮.zip", file, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(file)));
    }

    [Fact]
    public void ALongExtensionIsShortenedWithTheRest()
    {
        string file = LibraryNames.File("a." + new string('b', 300));

        Assert.Equal(LibraryNames.MaxFileBytes, Encoding.UTF8.GetByteCount(file));
    }

    [Fact]
    public void ANameShortenedToEndInADotDropsIt()
    {
        string folder = LibraryNames.Folder(new string('a', LibraryNames.MaxFolderBytes - 1) + ". more");

        Assert.Equal(new string('a', LibraryNames.MaxFolderBytes - 1), folder);
    }
}
