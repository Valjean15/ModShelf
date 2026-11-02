using ModShelf.Application.Settings;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

public sealed class PicturesViewModelTests
{
    private const long Megabyte = 1024 * 1024;

    private readonly SettingsHarness _harness = new();

    private PicturesViewModel Card => _harness.PicturesCard;

    [Fact]
    public async Task ShowsTheDefaultsAndTheRoomThePicturesTake()
    {
        using CultureScope culture = new("en-US");
        _harness.Pictures.Cache.Size = 84 * Megabyte;

        await _harness.StartAsync();

        Assert.Equal(["256 MB", "512 MB (the default)", "1 GB", "2 GB"], Card.SizeChoices.Select(choice => choice.Label));
        Assert.Equal(512, Card.SelectedSize?.Value);
        Assert.Equal("84.0 MB of 512 MB. The oldest go first when it's full.", Card.SizeText);
        Assert.Equal(84.0 / 512 * 100, Card.UsedPercent, 6);
        Assert.Equal(["6 hours", "1 day (the default)", "1 week"], Card.AgeChoices.Select(choice => choice.Label));
        Assert.Equal(TimeSpan.FromDays(1), Card.SelectedAge?.Value);
        Assert.Equal(["Hidden", "Blurred (the default)", "Shown"], Card.AdultChoices.Select(choice => choice.Label));
        Assert.Equal(AdultPictures.Blurred, Card.SelectedAdult?.Value);
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task OffersTheSizeAndAgeSetWhenTheyreOthers()
    {
        _harness.Settings.Stored = new AppSettings { PicturesMegabytes = 3072, DetailsMaxAge = TimeSpan.FromHours(12) };

        await _harness.StartAsync();

        Assert.Equal(["256 MB", "512 MB (the default)", "1 GB", "2 GB", "3 GB"], Card.SizeChoices.Select(choice => choice.Label));
        Assert.Equal(3072, Card.SelectedSize?.Value);
        Assert.Equal(["6 hours", "12 hours", "1 day (the default)", "1 week"], Card.AgeChoices.Select(choice => choice.Label));
        Assert.Equal(TimeSpan.FromHours(12), Card.SelectedAge?.Value);
    }

    [Fact]
    public async Task SavesEachChoiceAsItsMade()
    {
        using CultureScope culture = new("en-US");
        _harness.Pictures.Cache.Size = 100 * Megabyte;
        await _harness.StartAsync();

        Card.SelectedSize = Card.SizeChoices[3];
        await Card.Saving;
        Card.SelectedAge = Card.AgeChoices[2];
        await Card.Saving;
        Card.SelectedAdult = Card.AdultChoices[0];
        await Card.Saving;
        Card.SelectedSize = Card.SizeChoices[1];
        await Card.Saving;

        Assert.Equal(
            ["picturesMegabytes 2048", "detailsMaxAge 7.00:00:00", "adultPictures Hidden", "picturesMegabytes null"],
            _harness.Settings.Changes);
        Assert.Equal(AdultPictures.Hidden, _harness.Pictures.Pictures.Adult);
        Assert.Equal("100 MB of 512 MB. The oldest go first when it's full.", Card.SizeText);
        Assert.Null(Card.Problem);
    }

    [Fact]
    public async Task SaysWhyAChoiceCouldntBeSavedAndShowsWhatsStored()
    {
        await _harness.StartAsync();
        _harness.Settings.SaveRefusal = new IOException("The disk is full.");

        Card.SelectedAdult = Card.AdultChoices[2];
        await Card.Saving;

        Assert.Equal("The disk is full.", Card.Problem);
        Assert.Equal(AdultPictures.Blurred, Card.SelectedAdult?.Value);
        Assert.Equal(AdultPictures.Blurred, _harness.Pictures.Pictures.Adult);
    }

    [Fact]
    public async Task ClearsTheSavedPictures()
    {
        _harness.Pictures.Cache.Size = 84 * Megabyte;
        await _harness.StartAsync();
        bool couldClear = Card.ClearCommand.CanExecute(null);

        await Card.ClearCommand.ExecuteAsync(null);

        Assert.True(couldClear);
        Assert.Equal(1, _harness.Pictures.Cache.Clears);
        Assert.Equal(0, Card.UsedPercent);
        Assert.False(Card.ClearCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaysWhyThePicturesCouldntAllBeCleared()
    {
        _harness.Pictures.Cache.Size = 84 * Megabyte;
        _harness.Pictures.Cache.ClearRefusal = new IOException("A picture is in use.");
        await _harness.StartAsync();

        await Card.ClearCommand.ExecuteAsync(null);

        Assert.Equal("The saved pictures couldn't all be deleted: A picture is in use.", Card.Problem);
    }

    [Fact]
    public async Task ShowsTheDefaultsWhenTheSettingsCantBeRead()
    {
        _harness.Settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");

        await _harness.StartAsync();

        Assert.Equal(512, Card.SelectedSize?.Value);
        Assert.Equal(AdultPictures.Blurred, Card.SelectedAdult?.Value);
        Assert.Empty(_harness.Settings.Changes);
    }
}
