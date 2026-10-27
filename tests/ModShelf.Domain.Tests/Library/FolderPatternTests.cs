using ModShelf.Domain.Library;

namespace ModShelf.Domain.Tests.Library;

public sealed class FolderPatternTests
{
    [Fact]
    public void TheDefaultNamesTheModAndItsId()
    {
        Assert.Equal("{source}/{game}/{mod} ({modid})/{file}", FolderPattern.Default.Text);
        Assert.Equal(
            "nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip",
            FolderPattern.Default.Render("nexus", "stardewvalley", "SMAPI", "2400", "SMAPI 4.5.2.zip"));
    }

    [Theory]
    [InlineData("{file}", "SkyUI_5_2_SE.7z")]
    [InlineData("{source}/{game}/{modid}/{file}", "nexus/skyrimspecialedition/12604/SkyUI_5_2_SE.7z")]
    [InlineData("Mods/{game}/{mod}/{file}", "Mods/skyrimspecialedition/SkyUI/SkyUI_5_2_SE.7z")]
    [InlineData("{game} - {mod}/{file}", "skyrimspecialedition - SkyUI/SkyUI_5_2_SE.7z")]
    public void FillsInEveryPart(string pattern, string path)
    {
        Assert.Equal(path, FolderPattern.Parse(pattern).Render("nexus", "skyrimspecialedition", "SkyUI", "12604", "SkyUI_5_2_SE.7z"));
    }

    [Fact]
    public void MakesEachNameSafeForEverySystem()
    {
        string path = FolderPattern.Default.Render("nexus", "skyrimspecialedition", "Immersive Armors: Remastered / CON.", "3479", "Armor <HD>.7z");

        Assert.Equal("nexus/skyrimspecialedition/Immersive Armors_ Remastered _ CON. (3479)/Armor _HD_.7z", path);
    }

    [Fact]
    public void ANameThatLooksLikeAPartStaysAsItIs()
    {
        Assert.Equal("{modid}/x.zip", FolderPattern.Parse("{mod}/{file}").Render("nexus", "game", "{modid}", "1", "x.zip"));
    }

    [Fact]
    public void ANameWithASlashStaysOneFolder()
    {
        Assert.Equal("AC_DC Pack/x.zip", FolderPattern.Parse("{mod}/{file}").Render("nexus", "game", "AC/DC Pack", "1", "x.zip"));
    }

    [Theory]
    [InlineData("{source}/{game}/{mod}", "The last part must be exactly {file}.")]
    [InlineData("{source}/{game}/{mod}-{file}", "The last part must be exactly {file}.")]
    [InlineData("/{game}/{file}", "empty folder")]
    [InlineData("{game}//{file}", "empty folder")]
    [InlineData("{game}\\{file}", "Separate folders with '/'.")]
    [InlineData("{game}/{name}/{file}", "{name} isn't a folder's part.")]
    [InlineData("{file}/{file}", "{file} isn't a folder's part.")]
    [InlineData("{game}/{mod/{file}", "'{' or '}'")]
    [InlineData("{game}/mod}/{file}", "'{' or '}'")]
    [InlineData("C:/{game}/{file}", "isn't a folder name every system takes")]
    [InlineData("{game}/../{file}", "isn't a folder name every system takes")]
    [InlineData("Mods./{file}", "isn't a folder name every system takes")]
    [InlineData("CON/{file}", "isn't a folder name every system takes")]
    [InlineData("{game}/Mods?/{file}", "isn't a folder name every system takes")]
    public void SaysWhatsWrongWithAPattern(string pattern, string problem)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => FolderPattern.Parse(pattern));

        Assert.Contains(problem, refused.Message, StringComparison.Ordinal);
        Assert.StartsWith($"'{pattern}' isn't a folder pattern.", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RefusesABlankPattern(string pattern)
    {
        Assert.Throws<ArgumentException>(() => FolderPattern.Parse(pattern));
    }

    [Fact]
    public void PatternsWithTheSameTextAreEqual()
    {
        Assert.Equal(FolderPattern.Default, FolderPattern.Parse("{source}/{game}/{mod} ({modid})/{file}"));
        Assert.Equal("{mod}/{file}", FolderPattern.Parse("{mod}/{file}").ToString());
    }
}
