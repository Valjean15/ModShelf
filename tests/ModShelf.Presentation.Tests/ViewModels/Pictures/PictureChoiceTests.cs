using ModShelf.Application.Settings;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Pictures;

public sealed class PictureChoiceTests
{
    private static readonly Uri Picture = new("https://staticdelivery.nexusmods.com/mods/1704/images/12604/picture.png");
    private static readonly Uri Thumbnail = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/thumbnail.png");
    private static readonly Uri Blurred = new("https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/blurred.png");

    [Theory]
    [InlineData(AdultPictures.Hidden)]
    [InlineData(AdultPictures.Blurred)]
    [InlineData(AdultPictures.Shown)]
    public void AModsThumbnailShowsAsItIs(AdultPictures adult)
    {
        PictureChoice choice = PictureChoice.Of(Details(adult: false, Picture, Thumbnail, Blurred), adult);

        Assert.Equal(new PictureChoice(Thumbnail, IsBlurred: false, IsHidden: false, IsAdultBlurred: false), choice);
    }

    [Fact]
    public void ItsPictureStandsInForAThumbnailItsSiteDoesntGive()
    {
        Assert.Equal(Picture, PictureChoice.Of(Details(adult: false, Picture, thumbnail: null, blurred: null), AdultPictures.Blurred).Address);
        Assert.Null(PictureChoice.Of(Details(adult: false, picture: null, thumbnail: null, blurred: null), AdultPictures.Blurred).Address);
        Assert.Equal(default, PictureChoice.Of(null, AdultPictures.Blurred));
    }

    [Fact]
    public void AnAdultModsPictureShowsBlurredByItsSite()
    {
        PictureChoice choice = PictureChoice.Of(Details(adult: true, Picture, Thumbnail, Blurred), AdultPictures.Blurred);

        Assert.Equal(new PictureChoice(Blurred, IsBlurred: false, IsHidden: false, IsAdultBlurred: true), choice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAdultModsPictureItsSiteDoesntBlurIsBlurredHere(bool hasThumbnail)
    {
        PictureChoice choice = PictureChoice.Of(Details(adult: true, Picture, hasThumbnail ? Thumbnail : null, blurred: null), AdultPictures.Blurred);

        Assert.Equal(new PictureChoice(hasThumbnail ? Thumbnail : Picture, IsBlurred: true, IsHidden: false, IsAdultBlurred: true), choice);
    }

    [Fact]
    public void AnAdultModWithoutPicturesSaysItsBlurredAndBlursNothing()
    {
        PictureChoice choice = PictureChoice.Of(Details(adult: true, picture: null, thumbnail: null, blurred: null), AdultPictures.Blurred);

        Assert.Equal(new PictureChoice(null, IsBlurred: false, IsHidden: false, IsAdultBlurred: true), choice);
    }

    [Fact]
    public void AnAdultModsPictureCanBeHiddenOrShown()
    {
        ModInfo details = Details(adult: true, Picture, Thumbnail, Blurred);

        Assert.Equal(new PictureChoice(null, IsBlurred: false, IsHidden: true, IsAdultBlurred: false), PictureChoice.Of(details, AdultPictures.Hidden));
        Assert.Equal(new PictureChoice(Thumbnail, IsBlurred: false, IsHidden: false, IsAdultBlurred: false), PictureChoice.Of(details, AdultPictures.Shown));
    }

    private static ModInfo Details(bool adult, Uri? picture, Uri? thumbnail, Uri? blurred)
        => new(TestData.Mod("12604"), "SkyUI", pictureUrl: picture, hasAdultContent: adult, thumbnailUrl: thumbnail, thumbnailBlurredUrl: blurred);
}
