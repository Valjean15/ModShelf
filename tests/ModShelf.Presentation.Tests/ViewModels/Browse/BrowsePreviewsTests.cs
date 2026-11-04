using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

public sealed class BrowsePreviewsTests : IDisposable
{
    private static readonly Uri Thumbnail = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/1/one.png");
    private static readonly Uri Blurred = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/2/two-blurred.png");

    private readonly BrowseHarness _harness = new();

    public BrowsePreviewsTests()
    {
        ModRef one = new(BrowseHarness.Skyrim.Game, "1");
        ModRef two = new(BrowseHarness.Skyrim.Game, "2");
        _harness.Lookup.Mods[BrowseHarness.Skyrim.Game] =
        [
            new ModInfo(one, "Mod 1", thumbnailUrl: Thumbnail),
            new ModInfo(two, "Mod 2", hasAdultContent: true, thumbnailUrl: new Uri("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/2/two.png"), thumbnailBlurredUrl: Blurred),
        ];
    }

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsCardsWithTheSearchsPicturesByDefault()
    {
        await SearchAsync();

        Assert.True(Screen.Previews.IsCards);
        Assert.Equal([ScriptedPictureCache.FileOf(Thumbnail), ScriptedPictureCache.FileOf(Blurred)], Screen.Results.Select(row => row.Picture.File));
        Assert.True(Screen.Results[1].Picture.IsAdultBlurred);
        Assert.Empty(_harness.Pictures.Details.Asked);
    }

    [Fact]
    public async Task ShowsNoPicturesWithPreviewsOffThenShowsThemAsTheyreTurnedOn()
    {
        _harness.Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Browse, PreviewLevel.Off),
        };
        await SearchAsync();
        string?[] off = [.. Screen.Results.Select(row => row.Picture.File)];

        Screen.Previews.IsThumbnails = true;
        await PicturesShownAsync();

        Assert.Equal([null, null], off);
        Assert.Equal([ScriptedPictureCache.FileOf(Thumbnail), ScriptedPictureCache.FileOf(Blurred)], Screen.Results.Select(row => row.Picture.File));
        Assert.Equal(["preview Browse Thumbnails"], _harness.Pictures.Settings.Changes);
    }

    [Fact]
    public async Task AChosenGamesOwnPreviewsShowAndAChoiceChangesThem()
    {
        _harness.Pictures.Settings.Stored = new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer)
                .Add(BrowseHarness.Skyrim.Game, new GameSettings { Previews = PreviewLevel.Off }),
        };
        await SearchAsync();
        (bool, bool, string?) off = (Screen.Previews.IsOff, Screen.Previews.IsGamesOwn, Screen.Results[0].Picture.File);

        Screen.Previews.IsThumbnails = true;
        await PicturesShownAsync();
        await _harness.Pictures.Pictures.SavedAsync();

        Assert.Equal((true, true, (string?)null), off);
        Assert.Equal(ScriptedPictureCache.FileOf(Thumbnail), Screen.Results[0].Picture.File);
        Assert.Equal([$"game {BrowseHarness.Skyrim.Game}"], _harness.Pictures.Settings.Changes);
        Assert.Equal(PreviewLevel.Thumbnails, _harness.Pictures.Settings.Stored.GameFor(BrowseHarness.Skyrim.Game).Previews);
        Assert.Equal(PreviewLevel.Cards, _harness.Pictures.Pictures.LevelOf(PreviewScreen.Browse));
    }

    private async Task SearchAsync()
    {
        await _harness.StartAsync();
        await _harness.ShowAsync();
        await _harness.ChooseAndSearchAsync(BrowseHarness.Skyrim);
        await PicturesShownAsync();
    }

    private Task PicturesShownAsync()
        => Task.WhenAll(Screen.Results.Select(row => row.Picture.Showing))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
