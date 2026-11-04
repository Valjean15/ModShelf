using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// The example site, shaped unlike Nexus Mods, beside Nexus Mods on the Browse screen, asked as the app asks it.
/// </summary>
public sealed class BrowseExampleSiteTests : IDisposable
{
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly BrowseHarness _harness = new(example: new ExampleSite());

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsItsGamesBesideNexusModsOnesEachNamingItsSite()
    {
        await _harness.StartAsync();

        await _harness.ShowAsync();

        Assert.Equal(
            [
                "Fallout 4 (Nexus Mods)",
                "Pixel Farm (Example Mods)",
                "Skyrim Special Edition (Nexus Mods)",
                "Stardew Valley (Example Mods)",
                "Stardew Valley (Nexus Mods)",
            ],
            Screen.Games.Select(game => game.Name));
        Assert.False(Screen.GamesFailed);
    }

    [Fact]
    public async Task ItsGameShowsItsNewestAndUpdatedModsButNoTrendingOnes()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();

        await _harness.ChooseAsync(ExampleSite.StardewValley);

        Assert.False(Screen.Rows[0].IsShown);
        Assert.Equal(["Golden Hoes", "Better Crops"], Screen.Rows[1].Mods.Select(mod => mod.Name));
        Assert.Equal(["Better Crops", "Golden Hoes"], Screen.Rows[2].Mods.Select(mod => mod.Name));
    }

    [Fact]
    public async Task SearchingItsGameListsWhatTheSiteFinds()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.SearchText = "crops";

        await _harness.ChooseAndSearchAsync(ExampleSite.StardewValley);

        Assert.Equal(["Better Crops"], _harness.Names());
        Assert.Null(Screen.Problem);
    }

    [Fact]
    public async Task AnOrderTheSiteRefusesSaysWhyAndKeepsTheList()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        Screen.SearchText = "crops";
        await _harness.ChooseAndSearchAsync(ExampleSite.StardewValley);

        Screen.SelectedSort = Screen.Sorts.First(sort => sort.Sort == SearchSort.Endorsements);
        await Screen.Listing;

        Assert.Equal("Example Mods can't sort by Endorsements.", Screen.Problem);
        Assert.Equal(["Better Crops"], _harness.Names());
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task ItsModsLinkOpensItsPageWithEachVersionAMainFile()
    {
        await _harness.StartAsync();
        Screen.SearchText = ExampleSite.ModLink(Crops).AbsoluteUri;

        await Screen.SearchCommand.ExecuteAsync(null);

        ModPageViewModel page = Screen.Mod ?? throw new InvalidOperationException($"The mod didn't open: {Screen.Problem}");
        Assert.Equal("Better Crops", page.Name);
        ModFileSectionViewModel main = page.Sections[0];
        Assert.Equal(
            [ExampleSite.BetterCrops11, ExampleSite.BetterCrops10],
            main.Files.Select(file => file.File.Id));
    }
}
