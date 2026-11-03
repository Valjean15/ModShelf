using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Updates;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

/// <summary>
/// The updates the Updates screen leaves out: a pinned mod's, and the ones skipped; and Skip, which skips one.
/// </summary>
public sealed class UpdatesPinsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");

    private readonly UpdatesHarness _harness = new();

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task APinnedModsUpdatesAndSkippedOnesArentShownOrCounted()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53").WithUpdateSkipped(new ModFileRef(SkyUi, "53"), true),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7").WithPinned(true));

        Assert.Equal(["SkyUI 52"], Screen.Updates.Select(update => $"{update.ModName} {update.File.Id}"));
        Assert.Equal(1, Screen.Badge);
    }

    [Fact]
    public async Task AGameWhoseUpdatesArentOfferedIsntListed()
    {
        ModRef smapi = TestData.Mod("2400", "stardewvalley");
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"),
            UpdatesHarness.ModWithUpdates(smapi, "SMAPI", TestData.Now, "9").WithPinned(true));

        Assert.Equal([SkyUi.Game], Screen.Games.Select(game => game.Game));
    }

    [Fact]
    public async Task AModPinnedWhileTheScreenShowsLeavesIt()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));

        await _harness.Library.SetPinnedAsync(SkyUi, true, TestContext.Current.CancellationToken);

        Assert.Empty(Screen.Updates);
        Assert.Equal((false, 0), (Screen.HasUpdates, Screen.Badge));

        await _harness.Library.SetPinnedAsync(SkyUi, false, TestContext.Current.CancellationToken);

        Assert.Single(Screen.Updates);
    }

    [Fact]
    public async Task SkipSkipsTheUpdateWhichLeavesTheScreen()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"));

        await _harness.Row("53").SkipCommand.ExecuteAsync(null);

        Assert.Equal(["Skip 53"], _harness.Library.Asked);
        Assert.Equal(["52"], Screen.Updates.Select(update => update.File.Id));
        Assert.Equal([new ModFileRef(SkyUi, "53")], Assert.Single(_harness.Library.Mods).SkippedUpdates);
    }

    [Fact]
    public async Task SkippingAnUpdateThatLeftMeanwhileSaysNothing()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));
        UpdateViewModel row = _harness.Row("52");
        _harness.Library.Refusal = new KeyNotFoundException("No mod 12604.");

        await row.SkipCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task ASkipThatFailsUnexpectedlyIsLoggedAndSaid()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));
        UpdateViewModel row = _harness.Row("52");
        _harness.Library.Refusal = new InvalidOperationException("Something broke.");

        await row.SkipCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Messages);
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("Skip", StringComparison.Ordinal));
    }
}
