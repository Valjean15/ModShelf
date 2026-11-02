using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Pictures;

public sealed class ModPictureViewModelTests
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly Uri Old = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/old.png");
    private static readonly Uri New = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/new.png");
    private static readonly Uri Blurred = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/blurred.png");

    private readonly TestPictures _pictures = new();
    private readonly ModPictureViewModel _picture;

    public ModPictureViewModelTests() => _picture = new ModPictureViewModel(_pictures.Pictures);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowsTheSavedPictureThenTheSitesNewerOne()
    {
        _pictures.Details.Saved[SkyUi] = Details(Old);
        _pictures.Details.Site[SkyUi] = Details(New);
        _pictures.Details.SiteHeld = new TaskCompletionSource();
        List<string?> shown = [];
        _picture.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(ModPictureViewModel.File))
            {
                shown.Add(_picture.File);
            }
        };

        Task showing = _picture.ShowAsync(SkyUi, Cancellation);
        Assert.True(SpinWait.SpinUntil(() => _picture.File is not null, TimeSpan.FromSeconds(10)));
        string? saved = _picture.File;
        _pictures.Details.SiteHeld.SetResult();
        await showing;

        Assert.Equal(ScriptedPictureCache.FileOf(Old), saved);
        Assert.Equal(ScriptedPictureCache.FileOf(New), _picture.File);
        Assert.True(_picture.HasFile);
        Assert.Equal([ScriptedPictureCache.FileOf(Old), null, ScriptedPictureCache.FileOf(New)], shown);
        Assert.Equal(["saved 12604", "site 12604"], _pictures.Details.Asked);
    }

    [Fact]
    public async Task ReadsAPictureOnceWhenTheSiteNamesTheSavedOne()
    {
        _pictures.Details.Saved[SkyUi] = Details(Old);

        await _picture.ShowAsync(SkyUi, Cancellation);
        await _picture.RefreshAsync(Cancellation);

        Assert.Equal(ScriptedPictureCache.FileOf(Old), _picture.File);
        Assert.Equal([Old], _pictures.Cache.Asked);
    }

    [Fact]
    public async Task ShowsNoFileForAModWithoutDetailsOrAPictureThatCantBeDownloaded()
    {
        ModPictureViewModel other = new(_pictures.Pictures);
        _pictures.Details.Saved[SkyUi] = Details(Old);
        _pictures.Cache.Missing[Old] = true;

        await _picture.ShowAsync(TestData.Mod("1"), Cancellation);
        await other.ShowAsync(SkyUi, Cancellation);

        Assert.Null(_picture.File);
        Assert.False(_picture.HasFile);
        Assert.True(_picture.IsShown);
        Assert.True(_picture.ShowsPlaceholder);
        Assert.Null(other.File);
    }

    [Fact]
    public async Task AnAdultModsPictureLooksAsTheSettingsSay()
    {
        _pictures.Settings.Stored = new AppSettings { AdultPictures = AdultPictures.Hidden };
        await _pictures.Pictures.LoadAsync();
        ModInfo adult = new(SkyUi, "SkyUI", hasAdultContent: true, thumbnailUrl: Old, thumbnailBlurredUrl: Blurred);

        await _picture.ShowAsync(adult, Cancellation);
        (string?, bool, bool) hidden = (_picture.File, _picture.IsHidden, _picture.IsAdultBlurred);
        await _pictures.Pictures.SetAdultAsync(AdultPictures.Blurred, Cancellation);
        await _picture.RefreshAsync(Cancellation);
        (string?, bool, bool) blurred = (_picture.File, _picture.IsHidden, _picture.IsAdultBlurred);
        await _pictures.Pictures.SetAdultAsync(AdultPictures.Shown, Cancellation);
        await _picture.RefreshAsync(Cancellation);

        Assert.Equal((null, true, false), hidden);
        Assert.False(_picture.ShowsPlaceholder);
        Assert.Equal((ScriptedPictureCache.FileOf(Blurred), false, true), blurred);
        Assert.Equal(ScriptedPictureCache.FileOf(Old), _picture.File);
        Assert.False(_picture.IsBlurred);
        Assert.Equal([Blurred, Old], _pictures.Cache.Asked);
    }

    [Fact]
    public async Task BlursAnAdultModsPictureItsSiteDoesntBlur()
    {
        await _picture.ShowAsync(new ModInfo(SkyUi, "SkyUI", pictureUrl: Old, hasAdultContent: true), Cancellation);

        Assert.Equal(ScriptedPictureCache.FileOf(Old), _picture.File);
        Assert.True(_picture.IsBlurred);
        Assert.True(_picture.IsAdultBlurred);
    }

    [Fact]
    public async Task APictureAskedForLaterReplacesOneStillBeingRead()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        Task first = _picture.ShowAsync(Details(Old), Cancellation);
        _picture.Hide();
        Task second = _picture.ShowAsync(Details(New), Cancellation);

        _pictures.Cache.Held.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(ScriptedPictureCache.FileOf(New), _picture.File);
    }

    [Fact]
    public async Task HidingForgetsAPictureStillBeingRead()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        Task showing = _picture.ShowAsync(Details(Old), Cancellation);

        _picture.Hide();
        _pictures.Cache.Held.SetResult();
        await showing;
        await _picture.RefreshAsync(Cancellation);

        Assert.Null(_picture.File);
        Assert.False(_picture.IsAdultBlurred);
        Assert.False(_picture.IsShown);
    }

    [Fact]
    public async Task ShowingLogsWhatGoesWrongRatherThanThrowing()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        using CancellationTokenSource closing = new();

        _picture.Show(Details(Old), closing.Token);
        await closing.CancelAsync();
        await _picture.Showing;
        _pictures.Cache.Held = null;
        _pictures.Details.SiteHeld = new TaskCompletionSource();
        _pictures.Details.SiteHeld.SetException(new InvalidOperationException("Something broke."));
        _picture.Show(SkyUi, Cancellation);
        await _picture.Showing;

        Assert.Contains("Something broke.", Assert.Single(_pictures.Logger.Entries), StringComparison.Ordinal);
    }

    private static ModInfo Details(Uri thumbnail) => new(SkyUi, "SkyUI", thumbnailUrl: thumbnail);
}
