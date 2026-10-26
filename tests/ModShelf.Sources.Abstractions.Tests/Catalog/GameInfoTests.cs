using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Tests.Catalog;

public sealed class GameInfoTests
{
    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");

    [Fact]
    public void KeepsWhatTheSourceSays()
    {
        Uri page = new("https://www.nexusmods.com/stardewvalley");

        GameInfo game = new(StardewValley, "Stardew Valley", page);

        Assert.Same(StardewValley, game.Game);
        Assert.Equal("Stardew Valley", game.Name);
        Assert.Same(page, game.PageUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void NeedsAName(string name)
    {
        Assert.Throws<ArgumentException>(() => new GameInfo(StardewValley, name));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void RefusesAPageThatIsntAWebLink(string page)
    {
        Assert.Throws<ArgumentException>(() => new GameInfo(StardewValley, "Stardew Valley", new Uri(page)));
    }
}
