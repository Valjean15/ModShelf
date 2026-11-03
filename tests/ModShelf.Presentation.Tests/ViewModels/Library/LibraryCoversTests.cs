using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The games' covers on the Library screen: each game's wide picture from its site, read with its name, shown while the
/// screen shows pictures, as its covers are; its initial on its colour until then, and otherwise.
/// </summary>
public sealed class LibraryCoversTests : IDisposable
{
    private static readonly LibraryMod SkyUi = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI");
    private static readonly LibraryMod Address = TestData.LibraryMod(TestData.Mod("3863"), "Address Library");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");
    private static readonly Uri SkyrimCover = new("https://images.nexusmods.com/images/games/v2/1704/skyrim-hero.jpg");
    private static readonly Uri StardewCover = new("https://images.nexusmods.com/images/games/v2/1303/stardew-hero.jpg");

    private readonly LibraryHarness _harness = new();

    public LibraryCoversTests()
    {
        _harness.Catalog.GameCovers["skyrimspecialedition"] = SkyrimCover;
        _harness.Catalog.GameCovers["stardewvalley"] = StardewCover;
    }

    private LibraryViewModel Screen => _harness.Screen;

    private TestPictures Pictures => _harness.Pictures;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EachGameShowsTheCoverItsSiteGives()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";

        await _harness.StartAsync(SkyUi, Smapi);
        await CoversShownAsync();

        Assert.Equal(
            [("Skyrim Special Edition", ScriptedPictureCache.FileOf(SkyrimCover)), ("Stardew Valley", ScriptedPictureCache.FileOf(StardewCover))],
            Screen.Games.Select(game => (game.Name, game.Cover.File)));
        Assert.True(Screen.Games[0].Cover.HasFile);
    }

    [Fact]
    public async Task TheOpenGamesHeadingShowsItsCover()
    {
        await _harness.StartAsync(SkyUi);
        _harness.Row(SkyUi);
        await CoversShownAsync();

        Assert.Equal(ScriptedPictureCache.FileOf(SkyrimCover), Screen.OpenGame!.Cover.File);
    }

    [Fact]
    public async Task AGameItsSiteCantNameShowsItsInitialAndReadsNoPicture()
    {
        await _harness.StartAsync(Smapi);
        await CoversShownAsync();

        LibraryGameViewModel stardew = Assert.Single(Screen.Games);
        Assert.Equal(("S", null, false), (stardew.Initial, stardew.Cover.File, stardew.Cover.HasFile));
        Assert.Empty(Pictures.Cache.Asked);
    }

    [Fact]
    public async Task AGamesCoverIsReadOnceWhateverTheLibraryDoes()
    {
        await _harness.StartAsync(SkyUi, Address);

        _harness.Library.Change(SkyUi.Mod, null);
        _harness.Library.Change(SkyUi.Mod, SkyUi);
        await CoversShownAsync();

        Assert.Equal([SkyrimCover], Pictures.Cache.Asked);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyrimCover), Assert.Single(Screen.Games).Cover.File);
    }

    [Fact]
    public async Task ACoverShowsOnceItsSiteNamesTheGame()
    {
        _harness.Catalog.HoldGames = new TaskCompletionSource();
        await _harness.StartAsync(SkyUi);
        LibraryGameViewModel skyrim = Assert.Single(Screen.Games);
        string? before = skyrim.Cover.File;

        _harness.UiThread.Inline = false;
        _harness.Catalog.HoldGames.SetResult();
        Assert.True(SpinWait.SpinUntil(() => _harness.UiThread.Pending == 1, TimeSpan.FromSeconds(10)));
        _harness.UiThread.RunPending();
        await CoversShownAsync();

        Assert.Null(before);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyrimCover), skyrim.Cover.File);
    }

    [Fact]
    public async Task NoCoverIsReadWithPreviewsOffAndTurningThemOnShowsThem()
    {
        Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };
        await _harness.StartAsync(SkyUi);
        await CoversShownAsync();
        LibraryGameViewModel skyrim = Assert.Single(Screen.Games);
        (string?, int) whileOff = (skyrim.Cover.File, Pictures.Cache.Asked.Count);

        Screen.Previews.IsCards = true;
        await CoversShownAsync();
        string? asCards = skyrim.Cover.File;
        Screen.Previews.IsOff = true;
        string? offAgain = skyrim.Cover.File;
        Screen.Previews.IsThumbnails = true;
        await CoversShownAsync();

        Assert.Equal((null, 0), whileOff);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyrimCover), asCards);
        Assert.Null(offAgain);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyrimCover), skyrim.Cover.File);
    }

    [Fact]
    public async Task ACoverThatCantBeDownloadedLeavesTheInitial()
    {
        Pictures.Cache.Missing[SkyrimCover] = true;

        await _harness.StartAsync(SkyUi);
        await CoversShownAsync();

        LibraryGameViewModel skyrim = Assert.Single(Screen.Games);
        Assert.Equal(("S", null), (skyrim.Initial, skyrim.Cover.File));
        Assert.Equal([SkyrimCover], Pictures.Cache.Asked);
    }

    private Task CoversShownAsync()
        => Task.WhenAll(Screen.Games.Select(game => game.Cover.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
