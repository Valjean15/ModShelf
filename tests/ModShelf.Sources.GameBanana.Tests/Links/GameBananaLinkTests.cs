using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana.Tests.Links;

/// <summary>
/// Which of GameBanana's links name a mod or a file of one, and which aren't GameBanana's at all.
/// </summary>
public sealed class GameBananaLinkTests
{
    [Theory]
    [InlineData("https://gamebanana.com/mods/440088", "440088", null)]
    [InlineData("https://gamebanana.com/mods/440088/", "440088", null)]
    [InlineData("http://www.gamebanana.com/mods/440088?tab=files#Files", "440088", null)]
    [InlineData("https://GameBanana.com/Mods/440088", "440088", null)]
    [InlineData("https://gamebanana.com/mods/download/440088", "440088", null)]
    [InlineData("https://gamebanana.com/mmdl/1722096,Mod,440088", "440088", "1722096")]
    [InlineData("https://gamebanana.com/mmdl/1722096,mod,440088", "440088", "1722096")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1722096,Mod,440088", "440088", "1722096")]
    [InlineData("R2:https://gamebanana.com/mmdl/1722096,Mod,440088", "440088", "1722096")]
    public void ReadsAModOrAFileOfOne(string link, string mod, string? file)
    {
        Assert.Equal(new GameBananaLink(mod, file), GameBananaLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/stardewvalley/mods/2400")]
    [InlineData("https://files.gamebanana.com/mods/bgme_framework4_0_4.7z")]
    [InlineData("https://gamebanana.com.example.com/mods/440088")]
    [InlineData("jasm-plus:https://example.com/mmdl/1722096,Mod,440088")]
    [InlineData("jasm-plus:https://gamebanana.com/mods/440088")]
    [InlineData("file:///C:/gamebanana.com/mods/440088")]
    [InlineData("javascript:https://gamebanana.com/mmdl/1722096,Mod,440088")]
    [InlineData("mailto:someone@gamebanana.com")]
    public void IsntGameBanana(string link)
    {
        Assert.Null(GameBananaLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("https://gamebanana.com/dl/1722096")]
    [InlineData("https://gamebanana.com/mmdl/1722096")]
    [InlineData("R2:https://gamebanana.com/mmdl/1722096")]
    public void RefusesAFileWithoutItsMod(string link)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => GameBananaLink.Read(new Uri(link)));

        Assert.Contains("Copy the mod's page link", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://gamebanana.com/")]
    [InlineData("https://gamebanana.com/games/16951")]
    [InlineData("https://gamebanana.com/members/2986979")]
    [InlineData("https://gamebanana.com/sounds/440088")]
    [InlineData("https://gamebanana.com/mods/cats/18444")]
    [InlineData("https://gamebanana.com/mods/p5r")]
    [InlineData("https://gamebanana.com/mmdl/1722096,Wip,440088")]
    public void RefusesWhatIsntAModOrItsFile(string link)
    {
        Assert.Throws<ArgumentException>(() => GameBananaLink.Read(new Uri(link)));
    }

    [Theory]
    [InlineData("440088", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData("44a", false)]
    [InlineData("-1", false)]
    [InlineData("١٢٣", false)]
    [InlineData("1234567890123456789", false)]
    public void AnIdIsANumber(string text, bool isId)
    {
        Assert.Equal(isId, GameBananaLink.IsId(text));
    }

    [Fact]
    public void AModsPageIsOnGameBanana()
    {
        Assert.Equal(new Uri("https://gamebanana.com/mods/440088"), GameBananaLink.ModPage("440088"));
    }
}
