using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

public sealed class SameNamedGamesTests
{
    [Theory]
    [InlineData("Stardew Valley", "Stardew Valley")]
    [InlineData("Stardew Valley", "stardew valley")]
    [InlineData("Stardew Valley ", " Stardew Valley")]
    [InlineData("\tSTARDEW VALLEY\n", "Stardew Valley")]
    public void NamesAreTheSameIgnoringCaseAndSpacesAtTheirEnds(string first, string second)
    {
        Assert.True(SameNamedGames.Same(first, second));
        Assert.True(SameNamedGames.Same(second, first));
    }

    [Theory]
    [InlineData("Stardew Valley", "StardewValley")]
    [InlineData("Stardew Valley", "Stardew  Valley")]
    [InlineData("Skyrim", "Skyrim Special Edition")]
    public void NamesDifferingOtherwiseAreNot(string first, string second)
        => Assert.False(SameNamedGames.Same(first, second));

    [Fact]
    public void OnlyOtherSitesGamesOfTheNameAreTheSameGame()
    {
        GameRef nexus = new(new SourceId("nexus"), "stardewvalley");
        GameRef example = new(new SourceId("example"), "stardew-valley");
        GameRef nexusAgain = new(new SourceId("nexus"), "stardew");
        GameRef exampleOther = new(new SourceId("example"), "pixel-farm");

        IReadOnlyList<GameRef> same = SameNamedGames.Of(
            nexus,
            "Stardew Valley",
            [
                new(nexus, "Stardew Valley"),
                new(example, " stardew valley "),
                new(example, "Stardew Valley"),
                new(nexusAgain, "Stardew Valley"),
                new(exampleOther, "Pixel Farm"),
            ]);

        Assert.Equal([example], same);
    }
}
