using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.Tests.ViewModels.Pictures;

public sealed class PreviewsViewModelTests
{
    private readonly TestPictures _pictures = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PreviewScreen.Library, PreviewLevel.Thumbnails, true)]
    [InlineData(PreviewScreen.Browse, PreviewLevel.Cards, true)]
    [InlineData(PreviewScreen.Updates, PreviewLevel.Off, false)]
    [InlineData(PreviewScreen.Downloads, PreviewLevel.Off, false)]
    public async Task EachScreenStartsWithItsDefault(PreviewScreen screen, PreviewLevel level, bool offersCards)
    {
        PreviewsViewModel previews = new(screen, _pictures.Pictures);
        PreviewLevel before = previews.Level;

        await _pictures.Pictures.LoadAsync();
        previews.Refresh();

        Assert.Equal((level, level), (before, previews.Level));
        Assert.Equal(offersCards, previews.OffersCards);
    }

    [Fact]
    public async Task ShowsTheSettingsPreviews()
    {
        _pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty
                .Add(PreviewScreen.Library, PreviewLevel.Cards)
                .Add(PreviewScreen.Updates, PreviewLevel.Thumbnails),
        };
        PreviewsViewModel library = new(PreviewScreen.Library, _pictures.Pictures);
        PreviewsViewModel updates = new(PreviewScreen.Updates, _pictures.Pictures);

        await _pictures.Pictures.LoadAsync();
        library.Refresh();
        updates.Refresh();

        Assert.True(library.IsCards);
        Assert.False(library.ShowsRows);
        Assert.True(updates.IsThumbnails);
        Assert.True(updates.ShowsRows);
        Assert.True(updates.ShowsPictures);
    }

    [Fact]
    public async Task AScreenWithoutCardsShowsThumbnailsForThem()
    {
        _pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Downloads, PreviewLevel.Cards),
        };
        PreviewsViewModel downloads = new(PreviewScreen.Downloads, _pictures.Pictures);
        await _pictures.Pictures.LoadAsync();
        downloads.Refresh();

        downloads.IsCards = true;

        Assert.Equal(PreviewLevel.Thumbnails, downloads.Level);
        Assert.Empty(_pictures.Settings.Changes);
    }

    [Fact]
    public async Task SavesAChoiceAndTellsTheScreens()
    {
        PreviewsViewModel library = new(PreviewScreen.Library, _pictures.Pictures);
        await _pictures.Pictures.LoadAsync();
        library.Refresh();
        int changes = 0;
        _pictures.Pictures.Changed += (_, _) => changes++;

        library.IsCards = true;
        library.IsCards = false;
        library.IsOff = true;
        library.IsThumbnails = true;

        Assert.Equal(PreviewLevel.Thumbnails, library.Level);
        Assert.Equal(["preview Library Cards", "preview Library Off", "preview Library null"], _pictures.Settings.Changes);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task KeepsAChoiceThatCantBeSavedUntilTheAppCloses()
    {
        PreviewsViewModel browse = new(PreviewScreen.Browse, _pictures.Pictures);
        await _pictures.Pictures.LoadAsync();
        _pictures.Settings.SaveRefusal = new IOException("The disk is full.");

        browse.IsOff = true;
        PreviewsViewModel later = new(PreviewScreen.Browse, _pictures.Pictures);
        later.Refresh();

        Assert.True(later.IsOff);
        Assert.Contains("The disk is full.", Assert.Single(_pictures.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsTheDefaultsWhenTheSettingsCantBeRead()
    {
        _pictures.Settings.Stored = new AppSettings { AdultPictures = AdultPictures.Shown };
        _pictures.Settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");

        await _pictures.Pictures.LoadAsync();
        await _pictures.Pictures.LoadAsync();

        Assert.Equal(PreviewLevel.Cards, _pictures.Pictures.LevelOf(PreviewScreen.Browse));
        Assert.Equal(AdultPictures.Blurred, _pictures.Pictures.Adult);
        Assert.Contains("settings.json isn't JSON.", Assert.Single(_pictures.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavesHowAdultPicturesLookThenTellsTheScreens()
    {
        await _pictures.Pictures.LoadAsync();
        int changes = 0;
        _pictures.Pictures.Changed += (_, _) => changes++;

        await _pictures.Pictures.SetAdultAsync(AdultPictures.Hidden, Cancellation);
        await _pictures.Pictures.SetAdultAsync(AdultPictures.Hidden, Cancellation);
        await _pictures.Pictures.SetAdultAsync(AdultPictures.Blurred, Cancellation);

        Assert.Equal(AdultPictures.Blurred, _pictures.Pictures.Adult);
        Assert.Equal(["adultPictures Hidden", "adultPictures Hidden", "adultPictures null"], _pictures.Settings.Changes);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task AdultPicturesThatCantBeSavedStayAsTheyWere()
    {
        await _pictures.Pictures.LoadAsync();
        _pictures.Settings.SaveRefusal = new IOException("The disk is full.");

        await Assert.ThrowsAsync<IOException>(() => _pictures.Pictures.SetAdultAsync(AdultPictures.Shown, Cancellation));

        Assert.Equal(AdultPictures.Blurred, _pictures.Pictures.Adult);
    }
}
