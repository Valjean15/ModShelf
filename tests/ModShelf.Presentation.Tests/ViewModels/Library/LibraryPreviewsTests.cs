using System.Collections.Concurrent;
using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

public sealed class LibraryPreviewsTests : IDisposable
{
    private static readonly LibraryMod SkyUi = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");
    private static readonly Uri SkyUiPicture = new("https://staticdelivery.nexusmods.com/mods/1704/images/12604/skyui.png");
    private static readonly Uri SmapiPicture = new("https://staticdelivery.nexusmods.com/mods/1303/images/2400/smapi.png");

    private readonly LibraryHarness _harness = new();

    public LibraryPreviewsTests()
    {
        Details[SkyUi.Mod] = new ModInfo(SkyUi.Mod, "SkyUI", pictureUrl: SkyUiPicture);
        Details[Smapi.Mod] = new ModInfo(Smapi.Mod, "SMAPI", pictureUrl: SmapiPicture, hasAdultContent: true);
    }

    private LibraryViewModel Screen => _harness.Screen;

    private TestPictures Pictures => _harness.Pictures;

    private ConcurrentDictionary<ModRef, ModInfo> Details => Pictures.Details.Saved;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsThumbnailsByDefault()
    {
        await _harness.StartAsync(SkyUi, Smapi);
        await PicturesShownAsync();

        Assert.True(Screen.Previews.IsThumbnails);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyUiPicture), _harness.Row(SkyUi).Picture.File);
        Assert.Equal(ScriptedPictureCache.FileOf(SmapiPicture), _harness.Row(Smapi).Picture.File);
        Assert.True(_harness.Row(Smapi).Picture.IsBlurred);
    }

    [Fact]
    public async Task ShowsNoPicturesAndReadsNoneWithPreviewsOff()
    {
        Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };

        await _harness.StartAsync(SkyUi);
        await PicturesShownAsync();

        Assert.True(Screen.Previews.IsOff);
        Assert.Null(_harness.Row(SkyUi).Picture.File);
        Assert.Empty(Pictures.Details.Asked);
    }

    [Fact]
    public async Task TurningPreviewsOnShowsTheModsPicturesAndOffHidesThem()
    {
        Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };
        await _harness.StartAsync(SkyUi);

        Screen.Previews.IsCards = true;
        await PicturesShownAsync();
        string? shown = _harness.Row(SkyUi).Picture.File;
        Screen.Previews.IsOff = true;

        Assert.Equal(ScriptedPictureCache.FileOf(SkyUiPicture), shown);
        Assert.Null(_harness.Row(SkyUi).Picture.File);
        Assert.Equal(["preview Library Cards", "preview Library Off"], Pictures.Settings.Changes);
    }

    [Fact]
    public async Task AModAddedLaterShowsItsPicture()
    {
        await _harness.StartAsync(SkyUi);

        _harness.Library.Change(Smapi.Mod, Smapi);
        await PicturesShownAsync();

        Assert.Equal(ScriptedPictureCache.FileOf(SmapiPicture), _harness.Row(Smapi).Picture.File);
    }

    [Fact]
    public async Task AdultPicturesFollowTheSettingsAsTheyChange()
    {
        await _harness.StartAsync(Smapi);
        await PicturesShownAsync();

        await Pictures.Pictures.SetAdultAsync(AdultPictures.Hidden, TestContext.Current.CancellationToken);
        await PicturesShownAsync();

        Assert.True(_harness.Row(Smapi).Picture.IsHidden);
        Assert.Null(_harness.Row(Smapi).Picture.File);
    }

    [Fact]
    public async Task CardsShowEachGamesModsUnderItsHeading()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        LibraryMod address = TestData.LibraryMod(TestData.Mod("3863"), "Address Library");

        await _harness.StartAsync(SkyUi, Smapi, address);
        Screen.Filter = "s";

        Assert.Equal(["Skyrim Special Edition", "Stardew Valley"], Screen.Games.Select(game => game.Name));
        Assert.Equal(["Address Library", "SkyUI"], Screen.Games[0].Mods.Select(mod => mod.Name));
        Assert.Equal(["SMAPI"], Screen.Games[1].Mods.Select(mod => mod.Name));
    }

    private Task PicturesShownAsync()
        => Task.WhenAll(Screen.Rows.OfType<LibraryModViewModel>().Select(row => row.Picture.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
