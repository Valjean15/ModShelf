using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.ModPages;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library as quick search opens it: at a mod's page, its game's mods behind it, or at a game's mods, out of a page
/// or the recent activity.
/// </summary>
public sealed class LibraryQuickSearchTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly LibraryMod InLibrary = TestData.LibraryMod(SkyUi, "SkyUI");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task AModOpensAtItsPageOutOfTheRecentActivity()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"));
        await _harness.StartAsync(InLibrary, Smapi);
        _harness.Row(Smapi);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        _harness.Screens.OpenMod(SkyUi);
        await Screen.Opening;

        Assert.Equal("SkyUI", Assert.IsType<ModPageViewModel>(Screen.Mod).Name);
        Assert.Equal((false, "skyrimspecialedition"), (Screen.IsShowingActivity, Screen.OpenGame?.Game.Key));
        Assert.Equal("Skyrim Special Edition", Screen.BackText);
        Assert.Equal(Screen.Screen, _harness.Navigation.Current);
    }

    [Fact]
    public async Task AGameOpensAtItsModsOutOfAModsPage()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"));
        await _harness.StartAsync(InLibrary, Smapi);
        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        _harness.Screens.OpenInLibrary(Smapi.Mod.Game);

        Assert.Null(Screen.Mod);
        Assert.Equal("stardewvalley", Screen.OpenGame?.Game.Key);
        Assert.Equal(["SMAPI"], Screen.GameMods.Select(mod => mod.Name));
        Assert.Equal(Screen.Screen, _harness.Navigation.Current);
    }

    [Fact]
    public async Task AGameOpensOutOfTheRecentActivity()
    {
        await _harness.StartAsync(InLibrary, Smapi);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        _harness.Screens.OpenInLibrary(InLibrary.Mod.Game);

        Assert.Equal((false, "skyrimspecialedition"), (Screen.IsShowingActivity, Screen.OpenGame?.Game.Key));
    }
}
