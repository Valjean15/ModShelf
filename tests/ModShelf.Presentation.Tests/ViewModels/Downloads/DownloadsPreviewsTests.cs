using System.Collections.Immutable;

using ModShelf.Application.Downloads;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

public sealed class DownloadsPreviewsTests : IDisposable
{
    private static readonly Uri Thumbnail = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/skyui.png");

    private readonly DownloadsHarness _harness = new();

    public DownloadsPreviewsTests()
        => _harness.Pictures.Details.Saved[TestData.File().Mod] = new ModInfo(TestData.File().Mod, "SkyUI", thumbnailUrl: Thumbnail);

    private DownloadsViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsNoThumbnailsByDefault()
    {
        await _harness.StartWithAsync(TestData.Job("a.7z"));

        Assert.True(Screen.Previews.IsOff);
        Assert.Null(Screen.Jobs[0].Picture.File);
        Assert.Empty(_harness.Pictures.Details.Asked);
    }

    [Fact]
    public async Task ShowsEachDownloadsModThumbnailAsTheSettingsSay()
    {
        _harness.Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Downloads, PreviewLevel.Thumbnails),
        };
        await _harness.StartWithAsync(TestData.Job("a.7z"));

        DownloadJobSnapshot added = TestData.Job("b.7z", createdAt: TestData.Now.AddMinutes(1));
        _harness.Downloads.Publish(added);
        Assert.True(SpinWait.SpinUntil(() => Screen.Jobs.Count == 2, TimeSpan.FromSeconds(10)));
        await Task.WhenAll(Screen.Jobs.Select(job => job.Picture.Showing)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal([ScriptedPictureCache.FileOf(Thumbnail), ScriptedPictureCache.FileOf(Thumbnail)], Screen.Jobs.Select(job => job.Picture.File));
    }
}
