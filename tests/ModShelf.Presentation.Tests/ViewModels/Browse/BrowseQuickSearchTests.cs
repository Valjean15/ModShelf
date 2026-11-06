using ModShelf.Presentation.ViewModels.Browse;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse as quick search opens it, on a game: once the games are read when it's the first time, rather than the game
/// the library has most mods of; at once afterwards.
/// </summary>
public sealed class BrowseQuickSearchTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OpensOnTheGameAskedForOnceTheGamesAreRead()
    {
        await _harness.StartAsync(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));

        _harness.Screens.OpenInBrowse(BrowseHarness.Fallout.Game);
        await Screen.GamesWork;
        await Screen.DefaultsWork;

        Assert.Equal((Screen.Screen, BrowseHarness.Fallout.Game), (_harness.Navigation.Current, Screen.Game?.Game));
    }

    [Fact]
    public async Task OnceTheGamesAreReadOpensOnTheGameAtOnce()
    {
        await _harness.StartAsync(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        await _harness.ShowAsync();
        Assert.Equal(BrowseHarness.Skyrim.Game, Screen.Game?.Game);
        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Library);

        _harness.Screens.OpenInBrowse(BrowseHarness.Stardew.Game);

        Assert.Equal((Screen.Screen, BrowseHarness.Stardew.Game), (_harness.Navigation.Current, Screen.Game?.Game));
    }
}
