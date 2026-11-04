using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

/// <summary>
/// The example site's updates beside Nexus Mods ones: each under its own game, though both are Stardew Valley, and
/// read with the site's own lookup, which has no changelogs to give.
/// </summary>
public sealed class UpdatesExampleSiteTests : IDisposable
{
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Crops = ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops);

    private readonly UpdatesHarness _harness = new(new ExampleSite());

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EachSitesUpdatesAreUnderItsOwnGame()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(Crops, "Better Crops", TestData.Now, "v5e6f"));

        Assert.Equal(
            [(Smapi.Game, "Stardew Valley", "9"), (Crops.Game, "Stardew Valley", "v5e6f")],
            Screen.Games.Select(game => (game.Game, game.Name, Assert.Single(game.Updates).File.Id)).OrderBy(game => game.Game.Source.Value == "example"));
    }

    [Fact]
    public async Task AnUpdateOfASiteWithoutChangelogsSaysSo()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(Crops, "Better Crops", TestData.Now, "v5e6f"));

        Screen.OnShown();
        await Screen.ReadingChanges.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("The changelog says nothing about this version.", Assert.Single(Screen.Updates).ChangesNote);
    }
}
