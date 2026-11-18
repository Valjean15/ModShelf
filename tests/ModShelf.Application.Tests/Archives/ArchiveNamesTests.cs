using ModShelf.Application.Archives;

namespace ModShelf.Application.Tests.Archives;

/// <summary>
/// The names Nexus Mods gives its archives, which carry the mod's ID, and archives told by their extension.
/// </summary>
public sealed class ArchiveNamesTests
{
    [Theory]
    [InlineData("SkyUI_5_2_SE-12604-5-2SE-1573160372.7z", "12604")]
    [InlineData("Unofficial Skyrim Special Edition Patch-266-4-2-6a-1636402498.7z", "266")]
    [InlineData("SMAPI 4.5.2-2400-4-5-2-1760000000.zip", "2400")]
    [InlineData("No Version-77-1600000000.rar", "77")]
    [InlineData("SkyUI_5_2_SE-12604-5-2SE-1573160372 (1).7z", "12604")]
    [InlineData("SkyUI_5_2_SE-12604-5-2SE-1573160372.7Z", "12604")]
    public void ReadsTheModsIdFromANameNexusModsGave(string name, string modId)
    {
        Assert.Equal(modId, ArchiveNames.ModIdOf(name));
    }

    [Theory]
    [InlineData("SkyUI_5_2_SE.7z")]
    [InlineData("SkyUI-12604.7z")]
    [InlineData("Mod-1573160372.7z")]
    [InlineData("Mod-0-1-1573160372.7z")]
    [InlineData("-12604-1573160372.7z")]
    [InlineData("SkyUI-12604-5-2-1573160372")]
    public void AnyOtherNameNamesNoMod(string name)
    {
        Assert.Null(ArchiveNames.ModIdOf(name));
    }

    [Theory]
    [InlineData("SkyUI.7z", true)]
    [InlineData("SkyUI.ZIP", true)]
    [InlineData("SkyUI.rar", true)]
    [InlineData("SkyUI.7z.meta", false)]
    [InlineData("Readme.txt", false)]
    [InlineData("SkyUI.7z.part", false)]
    public void AnArchiveIsA7zZipOrRar(string name, bool isArchive)
    {
        Assert.Equal(isArchive, ArchiveNames.IsArchive(name));
    }
}
