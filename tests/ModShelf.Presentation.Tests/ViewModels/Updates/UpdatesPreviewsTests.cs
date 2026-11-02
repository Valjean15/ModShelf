using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

public sealed class UpdatesPreviewsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly Uri SkyUiThumbnail = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/skyui.png");
    private static readonly Uri AddressThumbnail = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/3863/address.png");

    private readonly UpdatesHarness _harness = new();

    public UpdatesPreviewsTests()
    {
        _harness.Pictures.Details.Saved[SkyUi] = new ModInfo(SkyUi, "SkyUI", thumbnailUrl: SkyUiThumbnail);
        _harness.Pictures.Details.Saved[Address] = new ModInfo(Address, "Address Library", thumbnailUrl: AddressThumbnail);
    }

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsNoThumbnailsByDefault()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));

        Assert.True(Screen.Previews.IsOff);
        Assert.False(Screen.Previews.OffersCards);
        Assert.Null(Screen.Updates[0].Picture.File);
        Assert.Empty(_harness.Pictures.Details.Asked);
    }

    [Fact]
    public async Task ShowsEachUpdatesModThumbnailOnceTurnedOn()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"));

        Screen.Previews.IsThumbnails = true;
        await PicturesShownAsync();

        Assert.Equal(
            [ScriptedPictureCache.FileOf(AddressThumbnail), ScriptedPictureCache.FileOf(SkyUiThumbnail), ScriptedPictureCache.FileOf(SkyUiThumbnail)],
            Screen.Updates.Select(update => update.Picture.File));
        Assert.Equal(["preview Updates Thumbnails"], _harness.Pictures.Settings.Changes);
    }

    [Fact]
    public async Task AnUpdateFoundLaterShowsItsThumbnail()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));
        Screen.Previews.IsThumbnails = true;

        _harness.Library.Change(Address, UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"));
        await PicturesShownAsync();

        Assert.Equal(ScriptedPictureCache.FileOf(AddressThumbnail), Assert.Single(Screen.Updates, update => update.File.Mod == Address).Picture.File);
    }

    private Task PicturesShownAsync()
        => Task.WhenAll(Screen.Updates.Select(update => update.Picture.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
