using System.Text;

using ModShelf.Application.Archives;

namespace ModShelf.Application.Tests.Archives;

/// <summary>
/// Mod Organizer 2's notes beside its archives, as its versions wrote them: UTF-8 or the system's code page, with or
/// without a byte order mark, values quoted and escaped as Qt's QSettings writes them.
/// </summary>
public sealed class ModOrganizerNotesTests
{
    [Fact]
    public void ReadsTheModAndTheFileOfANexusModsDownload()
    {
        ModOrganizerNote? note = Read(
            "[General]\r\ngameName=SkyrimSE\r\nmodID=12604\r\nfileID=35407\r\nurl=\r\nname=SkyUI 5.2\r\nmodName=SkyUI\r\nversion=5.2SE\r\n"
            + "repository=Nexus\r\ninstalled=true\r\n");

        Assert.Equal(new ModOrganizerNote("12604", "35407", "SkyUI"), note);
    }

    [Theory]
    [InlineData("[General]\nmodID=0\nfileID=35407\n")]
    [InlineData("[General]\nmodID=\n")]
    [InlineData("[General]\nmodID=-3\n")]
    [InlineData("[General]\nmodID=12604\nrepository=SomewhereElse\n")]
    [InlineData("[Other]\nmodID=12604\n")]
    [InlineData("")]
    [InlineData("not an INI file at all")]
    public void NamesNoModWhenItHasNoneOfNexusMods(string text)
    {
        Assert.Null(Read(text));
    }

    [Fact]
    public void ANoteWithoutAFileOrAModNameStillNamesTheMod()
    {
        Assert.Equal(new ModOrganizerNote("12604", null, "SkyUI 5.2"), Read("[general]\nModID = 12604 \nfileID=0\nname=\"SkyUI 5.2\"\n"));
        Assert.Equal(new ModOrganizerNote("12604", null, null), Read("; written by hand\n[General]\nmodID=12604\n"));
    }

    [Fact]
    public void ReadsTheSystemsCodePageAndUtf16AndQtsEscapes()
    {
        byte[] latin1 = Encoding.Latin1.GetBytes("[General]\nmodID=7\nmodName=Caf\u00e9\n");
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("[General]\nmodID=8\nmodName=\u65e5\u672c\n")];
        byte[] utf8 = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[General]\nmodID=9\nmodName=Na\u00efve\n")];
        byte[] escaped = Encoding.ASCII.GetBytes("[General]\nmodID=10\nmodName=\"Caf\\xe9, \\\"quoted\\\" \\\\ \\x65e5\"\n");

        Assert.Equal("Caf\u00e9", ModOrganizerNotes.Read(latin1)?.ModName);
        Assert.Equal(("8", "\u65e5\u672c"), (ModOrganizerNotes.Read(utf16)?.ModId, ModOrganizerNotes.Read(utf16)?.ModName));
        Assert.Equal("Na\u00efve", ModOrganizerNotes.Read(utf8)?.ModName);
        Assert.Equal("Caf\u00e9, \"quoted\" \\ \u65e5", ModOrganizerNotes.Read(escaped)?.ModName);
    }

    private static ModOrganizerNote? Read(string text) => ModOrganizerNotes.Read(Encoding.UTF8.GetBytes(text));
}
