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
    private static readonly LibraryMod Address = TestData.LibraryMod(TestData.Mod("3863"), "Address Library");
    private static readonly Uri SkyUiPicture = new("https://staticdelivery.nexusmods.com/mods/1704/images/12604/skyui.png");
    private static readonly Uri SmapiPicture = new("https://staticdelivery.nexusmods.com/mods/1303/images/2400/smapi.png");
    private static readonly Uri AddressPicture = new("https://staticdelivery.nexusmods.com/mods/1704/images/3863/address.png");

    private readonly LibraryHarness _harness = new();

    public LibraryPreviewsTests()
    {
        Details[SkyUi.Mod] = new ModInfo(SkyUi.Mod, "SkyUI", pictureUrl: SkyUiPicture);
        Details[Smapi.Mod] = new ModInfo(Smapi.Mod, "SMAPI", pictureUrl: SmapiPicture, hasAdultContent: true);
        Details[Address.Mod] = new ModInfo(Address.Mod, "Address Library", pictureUrl: AddressPicture);
    }

    private LibraryViewModel Screen => _harness.Screen;

    private TestPictures Pictures => _harness.Pictures;

    private ConcurrentDictionary<ModRef, ModInfo> Details => Pictures.Details.Saved;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsThumbnailsByDefault()
    {
        await _harness.StartAsync(SkyUi, Smapi);
        LibraryModViewModel skyUi = await ShownAsync(SkyUi);
        LibraryModViewModel smapi = await ShownAsync(Smapi);

        Assert.True(Screen.Previews.IsThumbnails);
        Assert.Equal(ScriptedPictureCache.FileOf(SkyUiPicture), skyUi.Picture.File);
        Assert.Equal(ScriptedPictureCache.FileOf(SmapiPicture), smapi.Picture.File);
        Assert.True(smapi.Picture.IsBlurred);
    }

    [Fact]
    public async Task ShowsNoPicturesAndReadsNoneWithPreviewsOff()
    {
        Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };

        await _harness.StartAsync(SkyUi);
        LibraryModViewModel skyUi = await ShownAsync(SkyUi);

        Assert.True(Screen.Previews.IsOff);
        Assert.Null(skyUi.Picture.File);

        // Only the saved details, for when its site last updated it: neither the site nor the picture.
        Assert.Equal(["saved 12604"], Pictures.Details.Asked);
        Assert.Empty(Pictures.Cache.Asked);
    }

    [Fact]
    public async Task TurningPreviewsOnShowsTheModsPicturesAndOffHidesThem()
    {
        Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };
        await _harness.StartAsync(SkyUi);
        await ShownAsync(SkyUi);

        Screen.Previews.IsCards = true;
        await PicturesShownAsync();
        string? shown = _harness.Row(SkyUi).Picture.File;
        Screen.Previews.IsOff = true;

        Assert.Equal(ScriptedPictureCache.FileOf(SkyUiPicture), shown);
        Assert.Null(_harness.Row(SkyUi).Picture.File);
        Assert.Equal(["preview Library Cards", "preview Library Off"], Pictures.Settings.Changes);
    }

    [Fact]
    public async Task AModAddedLaterToTheOpenGameShowsItsPicture()
    {
        await _harness.StartAsync(SkyUi);
        await ShownAsync(SkyUi);

        _harness.Library.Change(Address.Mod, Address);
        await PicturesShownAsync();

        Assert.Equal(ScriptedPictureCache.FileOf(AddressPicture), _harness.Row(Address).Picture.File);
    }

    [Fact]
    public async Task AdultPicturesFollowTheSettingsAsTheyChange()
    {
        await _harness.StartAsync(Smapi);
        await ShownAsync(Smapi);

        await Pictures.Pictures.SetAdultAsync(AdultPictures.Hidden, TestContext.Current.CancellationToken);
        await PicturesShownAsync();

        Assert.True(_harness.Row(Smapi).Picture.IsHidden);
        Assert.Null(_harness.Row(Smapi).Picture.File);
    }

    [Fact]
    public async Task OnlyTheOpenGamesModsReadTheirPictures()
    {
        await _harness.StartAsync(SkyUi, Smapi, Address);
        (int, int) beforeAGameOpened = (Pictures.Cache.Asked.Count, Pictures.Details.Asked.Count(asked => asked.StartsWith("site", StringComparison.Ordinal)));

        await ShownAsync(SkyUi);

        Assert.Equal((0, 0), beforeAGameOpened);
        Assert.Equal([SkyUiPicture, AddressPicture], Pictures.Cache.Asked.Order(Comparer<Uri>.Create((a, b) => string.CompareOrdinal(a.AbsoluteUri, b.AbsoluteUri))));
    }

    [Fact]
    public async Task ThePicturesShownStayWhenAnotherGameOpens()
    {
        await _harness.StartAsync(SkyUi, Smapi);
        LibraryModViewModel skyUi = await ShownAsync(SkyUi);

        await ShownAsync(Smapi);

        Assert.Equal(ScriptedPictureCache.FileOf(SkyUiPicture), skyUi.Picture.File);
        Assert.Single(Pictures.Cache.Asked, asked => asked == SkyUiPicture);
    }

    /// <summary>
    /// The mod's row, its game opened, once the open game's pictures show.
    /// </summary>
    private async Task<LibraryModViewModel> ShownAsync(LibraryMod mod)
    {
        LibraryModViewModel row = _harness.Row(mod);
        await Screen.Checking;
        await PicturesShownAsync();
        return row;
    }

    private Task PicturesShownAsync()
        => Task.WhenAll(Screen.GameMods.Select(row => row.Picture.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
