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
        Assert.Empty(game.Categories);
    }

    [Fact]
    public void KeepsEachCategoryOnceTrimmedInTheSitesOrder()
    {
        GameInfo game = new(StardewValley, "Stardew Valley", categories: ["Modding Tools", " Audio ", "", "Modding Tools", "  "]);

        Assert.Equal(["Modding Tools", "Audio"], game.Categories);
    }

    [Fact]
    public void GamesWithTheSameCategoriesAreEqual()
    {
        GameInfo game = new(StardewValley, "Stardew Valley", categories: ["Audio", "Maps"]);

        Assert.Equal(game, new GameInfo(StardewValley, "Stardew Valley", categories: ["Audio", "Maps"]));
        Assert.Equal(game.GetHashCode(), new GameInfo(StardewValley, "Stardew Valley", categories: ["Audio", "Maps"]).GetHashCode());
        Assert.NotEqual(game, new GameInfo(StardewValley, "Stardew Valley", categories: ["Maps", "Audio"]));
        Assert.NotEqual(game, new GameInfo(StardewValley, "Stardew Valley"));
        Assert.NotEqual(game, new GameInfo(StardewValley, "Stardew", categories: ["Audio", "Maps"]));
        Assert.False(game.Equals(null));
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
