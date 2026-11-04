using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The example site's mods in the Library beside Nexus Mods ones: Stardew Valley is on both, and stays two games.
/// </summary>
public sealed class LibraryExampleSiteTests : IDisposable
{
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly LibraryHarness _harness = new(new ExampleSite());

    public LibraryExampleSiteTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TheSameGameOnTwoSitesIsTwoGamesEachNamedByItsSite()
    {
        LibraryMod crops = TestData.LibraryMod(Crops, "Better Crops", files: TestData.LibraryFile(Crops, ExampleSite.BetterCrops11, "better-crops-1.1.zip"));

        await _harness.StartAsync(TestData.LibraryMod(Smapi, "SMAPI"), crops);

        LibraryGameViewModel nexus = _harness.Game(Smapi.Game);
        LibraryGameViewModel example = _harness.Game(Crops.Game);
        Assert.Equal(2, Screen.Games.Count);
        Assert.Equal(("Stardew Valley", "1 mod"), (nexus.Name, nexus.ModsText));
        Assert.Equal(("Stardew Valley", "1 mod"), (example.Name, example.ModsText));
        Assert.Equal("Better Crops", _harness.Row(crops).Name);
        Assert.Equal(Crops.Game, Screen.OpenGame?.Game);
    }

    [Fact]
    public async Task EachOfTheSameGameOnTwoSitesSaysWhereElseItIs()
    {
        LibraryMod skyUi = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI");

        await _harness.StartAsync(TestData.LibraryMod(Smapi, "SMAPI"), TestData.LibraryMod(Crops, "Better Crops"), skyUi);

        Assert.Equal(("Also on Example Mods", true), (_harness.Game(Smapi.Game).AlsoOnText, _harness.Game(Smapi.Game).HasAlsoOn));
        Assert.Equal("Also on Nexus Mods", _harness.Game(Crops.Game).AlsoOnText);
        Assert.Equal((string.Empty, false), (_harness.Game(skyUi.Mod.Game).AlsoOnText, _harness.Game(skyUi.Mod.Game).HasAlsoOn));
    }
}
