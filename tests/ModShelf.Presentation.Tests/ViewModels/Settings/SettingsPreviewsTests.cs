using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// General › Previews: each screen's previews, rows, thumbnails or, where it has them, cards, as stored; a choice saved
/// at once, and the screens showing it once it's saved.
/// </summary>
public sealed class SettingsPreviewsTests
{
    private readonly SettingsHarness _harness = new();

    private SettingsViewModel Screen => _harness.Screen;

    private ModPictures Pictures => _harness.Pictures.Pictures;

    [Fact]
    public async Task ShowsEachScreensPreviewsAsStored()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty
                .Add(PreviewScreen.Library, PreviewLevel.Cards)
                .Add(PreviewScreen.Downloads, PreviewLevel.Thumbnails),
        };

        await StartAsync();

        Assert.Equal(
            [
                ("Library", "Its games, a game's mods and a mod's page", true, PreviewLevel.Cards),
                ("Browse", "The mods a search finds, and a mod's page", true, PreviewLevel.Cards),
                ("Updates", null, false, PreviewLevel.Off),
                ("Downloads", null, false, PreviewLevel.Thumbnails),
            ],
            Screen.ScreenPreviews.Select(screen => (screen.Name, screen.Note, screen.OffersCards, screen.Level)));
        Assert.Equal((false, false, true), (Screen.ScreenPreviews[0].IsRows, Screen.ScreenPreviews[0].IsThumbnails, Screen.ScreenPreviews[0].IsCards));
        Assert.Equal((true, false), (Screen.ScreenPreviews[2].IsRows, Screen.ScreenPreviews[2].HasNote));
        await Screen.Saving;
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task AChoiceIsSavedAndTheScreensShowIt()
    {
        await StartAsync();
        int changes = 0;
        Pictures.Changed += (_, _) => changes++;

        Screen.ScreenPreviews[0].IsRows = true;
        Screen.ScreenPreviews[3].IsThumbnails = true;
        await Screen.Saving;

        Assert.Equal(["preview Library Off", "preview Downloads Thumbnails"], _harness.Settings.Changes);
        Assert.Equal((PreviewLevel.Off, PreviewLevel.Thumbnails), (Pictures.LevelOf(PreviewScreen.Library), Pictures.LevelOf(PreviewScreen.Downloads)));
        Assert.Equal(2, changes);
        Assert.Equal((PreviewLevel.Off, PreviewLevel.Thumbnails), (Screen.ScreenPreviews[0].Level, Screen.ScreenPreviews[3].Level));
    }

    [Fact]
    public async Task ChoosingTheDefaultForgetsTheScreensOwn()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Browse, PreviewLevel.Off),
        };
        await StartAsync();

        Screen.ScreenPreviews[1].IsCards = true;
        await Screen.Saving;

        Assert.Equal(["preview Browse null"], _harness.Settings.Changes);
        Assert.Empty(_harness.Settings.Stored.Previews);
        Assert.Equal(PreviewLevel.Cards, Pictures.LevelOf(PreviewScreen.Browse));
    }

    [Fact]
    public async Task CardsArentOfferedWhereTheScreenHasNone()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Updates, PreviewLevel.Cards),
        };
        await StartAsync();

        ScreenPreviewsViewModel updates = Screen.ScreenPreviews[2];
        updates.IsCards = true;
        updates.IsRows = false;
        await Screen.Saving;

        Assert.Equal((PreviewLevel.Thumbnails, false), (updates.Level, updates.IsCards));
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task AChoiceThatCantBeSavedSaysWhyAndShowsWhatsStored()
    {
        await StartAsync();
        _harness.Settings.SaveRefusal = new IOException("The disk is full.");

        Screen.ScreenPreviews[0].IsCards = true;
        await Screen.Saving;

        Assert.Equal("The disk is full.", Screen.Problem);
        Assert.Equal(PreviewLevel.Thumbnails, Screen.ScreenPreviews[0].Level);
        Assert.Equal(PreviewLevel.Thumbnails, Pictures.LevelOf(PreviewScreen.Library));
    }

    [Fact]
    public async Task AChoiceMadeInBrowseShowsOnceTheScreenIsShown()
    {
        await StartAsync();

        await Pictures.SetLevelAsync(PreviewScreen.Browse, PreviewLevel.Thumbnails);
        Screen.OnShown();
        await Screen.ShownWork;

        Assert.True(Screen.ScreenPreviews[1].IsThumbnails);
    }

    [Theory]
    [InlineData("previews")]
    [InlineData("thumbnails")]
    [InlineData("a mod's page")]
    public async Task ASearchFindsThem(string words)
    {
        await StartAsync();

        Screen.SearchText = words;

        Assert.True(Screen.PreviewsSection.IsShown);
    }

    private async Task StartAsync()
    {
        await Pictures.LoadAsync();
        await _harness.StartAsync();
    }
}
