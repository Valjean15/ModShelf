using ModShelf.Sources.CurseForge.Links;

namespace ModShelf.Sources.CurseForge.Tests.Links;

/// <summary>
/// Links to mods and files on CurseForge's website, read without asking CurseForge.
/// </summary>
public sealed class CurseForgeLinkTests
{
    [Theory]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu", null)]
    [InlineData("https://curseforge.com/Minecraft/MC-Mods/ModMenu/", null)]
    [InlineData("http://www.curseforge.com/minecraft/mc-mods/modmenu?tab=files", null)]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/files", null)]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/description", null)]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/files/5432112", 5432112L)]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/download/5432112", 5432112L)]
    public void ReadsAModsLinkAndAFilesLink(string link, long? file)
    {
        Assert.Equal(new CurseForgeLink("minecraft", "mc-mods", "modmenu", file), CurseForgeLink.Parse(new Uri(link)));
    }

    [Theory]
    [InlineData("https://www.curseforge.com/minecraft")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/comments")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/mod%20menu")]
    [InlineData("https://legacy.curseforge.com.example.com/minecraft/mc-mods/modmenu")]
    [InlineData("https://www.nexusmods.com/minecraft/mods/1")]
    [InlineData("ftp://www.curseforge.com/minecraft/mc-mods/modmenu")]
    public void LeavesOtherLinksAlone(string link)
    {
        Assert.Null(CurseForgeLink.Parse(new Uri(link)));
    }

    [Theory]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/files/latest")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/download")]
    [InlineData("https://www.curseforge.com/minecraft/mc-mods/modmenu/files/0")]
    public void RefusesAFilesLinkThatNamesNoFile(string link)
    {
        Assert.Throws<ArgumentException>(() => CurseForgeLink.Parse(new Uri(link)));
    }
}
