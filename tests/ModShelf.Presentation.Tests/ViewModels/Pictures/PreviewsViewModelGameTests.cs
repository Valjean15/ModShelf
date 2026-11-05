using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.Tests.ViewModels.Pictures;

/// <summary>
/// A game's own previews: shown where its mods are, its page in the Library and Browse with it chosen, in place of the
/// screen's; and changed by a choice there while it has them.
/// </summary>
public sealed class PreviewsViewModelGameTests
{
    private static readonly GameRef Stardew = TestData.Mod("2400", "stardewvalley").Game;
    private static readonly GameRef Skyrim = TestData.Mod("12604").Game;

    private readonly TestPictures _pictures = new();

    public PreviewsViewModelGameTests()
    {
        _pictures.Settings.Stored = new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer)
                .Add(new GameRef(TestData.Nexus, "StardewValley"), new GameSettings { Previews = PreviewLevel.Off }),
        };
    }

    private ModPictures Pictures => _pictures.Pictures;

    [Theory]
    [InlineData(PreviewScreen.Library, PreviewLevel.Thumbnails)]
    [InlineData(PreviewScreen.Browse, PreviewLevel.Cards)]
    public async Task AGamesOwnPreviewsShowWhereItsModsAre(PreviewScreen screen, PreviewLevel screens)
    {
        PreviewsViewModel previews = await StartedAsync(screen);

        previews.ShowFor(Stardew);
        (PreviewLevel, bool, string?) stardew = (previews.Level, previews.IsGamesOwn, previews.Hint);
        previews.ShowFor(Skyrim);
        (PreviewLevel, bool, string?) skyrim = (previews.Level, previews.IsGamesOwn, previews.Hint);
        previews.ShowFor(null);

        Assert.Equal(
            (PreviewLevel.Off, true, "This game has previews of its own, so a choice here changes them. Its settings, in Settings › Games, can set it back to the screen's."),
            stardew);
        Assert.Equal((screens, false, (string?)null), skyrim);
        Assert.Equal((screens, false), (previews.Level, previews.IsGamesOwn));
    }

    [Theory]
    [InlineData(PreviewScreen.Updates)]
    [InlineData(PreviewScreen.Downloads)]
    public async Task TheOtherScreensKeepTheirOwn(PreviewScreen screen)
    {
        _pictures.Settings.Stored = _pictures.Settings.Stored with
        {
            Games = _pictures.Settings.Stored.Games.ToImmutableDictionary(AppSettings.GameComparer)
                .SetItem(Stardew, new GameSettings { Previews = PreviewLevel.Cards }),
        };
        PreviewsViewModel previews = await StartedAsync(screen);

        previews.ShowFor(Stardew);

        Assert.Equal((PreviewLevel.Off, false), (previews.Level, previews.IsGamesOwn));
        Assert.Null(Pictures.OwnLevelOf(screen, Stardew));
    }

    [Fact]
    public async Task AChoiceChangesTheGamesOwnWhileItHasSome()
    {
        PreviewsViewModel library = await StartedAsync(PreviewScreen.Library);
        library.ShowFor(Stardew);
        int changes = 0;
        Pictures.Changed += (_, _) => changes++;

        library.IsCards = true;
        await Pictures.SavedAsync();
        PreviewsViewModel browse = await StartedAsync(PreviewScreen.Browse);
        browse.ShowFor(Stardew);

        Assert.Equal(["game nexus/stardewvalley"], _pictures.Settings.Changes);
        Assert.Equal(PreviewLevel.Cards, _pictures.Settings.Stored.GameFor(Stardew).Previews);
        Assert.Equal(PreviewLevel.Thumbnails, Pictures.LevelOf(PreviewScreen.Library));
        Assert.Equal((PreviewLevel.Cards, true), (browse.Level, browse.IsGamesOwn));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task AChoiceForAGameWithoutItsOwnChangesTheScreens()
    {
        PreviewsViewModel library = await StartedAsync(PreviewScreen.Library);
        library.ShowFor(Skyrim);

        library.IsCards = true;
        library.ShowFor(Stardew);

        Assert.Equal(["preview Library Cards"], _pictures.Settings.Changes);
        Assert.Same(GameSettings.None, _pictures.Settings.Stored.GameFor(Skyrim));
        Assert.Equal(PreviewLevel.Off, library.Level);
    }

    [Fact]
    public async Task AGamesPreviewsThatCantBeSavedStayUntilTheAppCloses()
    {
        PreviewsViewModel library = await StartedAsync(PreviewScreen.Library);
        library.ShowFor(Stardew);
        _pictures.Settings.SaveRefusal = new IOException("The disk is full.");

        library.IsThumbnails = true;
        await Pictures.SavedAsync();
        PreviewsViewModel later = await StartedAsync(PreviewScreen.Library);
        later.ShowFor(Stardew);

        Assert.Equal((PreviewLevel.Thumbnails, true), (later.Level, later.IsGamesOwn));
        Assert.Equal(PreviewLevel.Off, _pictures.Settings.Stored.GameFor(Stardew).Previews);
        string logged = Assert.Single(_pictures.Logger.Entries);
        Assert.Contains("nexus/stardewvalley", logged, StringComparison.Ordinal);
        Assert.Contains("The disk is full.", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavedWaitsForEveryChoiceToBeSaved()
    {
        PreviewsViewModel library = await StartedAsync(PreviewScreen.Library);
        library.ShowFor(Stardew);
        _pictures.Settings.Hold = new TaskCompletionSource();

        library.IsCards = true;
        library.ShowFor(Skyrim);
        library.IsOff = true;
        Task saved = Pictures.SavedAsync();
        bool before = saved.IsCompleted;
        _pictures.Settings.Hold.SetResult();
        await saved.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(before);
        Assert.Equal(["game nexus/stardewvalley", "preview Library Off"], _pictures.Settings.Changes);
        Assert.Equal(PreviewLevel.Cards, _pictures.Settings.Stored.GameFor(Stardew).Previews);
        Assert.Equal(PreviewLevel.Off, _pictures.Settings.Stored.PreviewFor(PreviewScreen.Library));
    }

    [Fact]
    public async Task ShowsTheGamesPreviewsTheSettingsScreenReadsTellingTheScreensOnlyOfAChange()
    {
        await Pictures.LoadAsync();
        int changes = 0;
        Pictures.Changed += (_, _) => changes++;
        AppSettings changed = new()
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer)
                .Add(Stardew, new GameSettings { Previews = PreviewLevel.Off })
                .Add(Skyrim, new GameSettings { Previews = PreviewLevel.Cards, Hidden = true }),
        };

        Pictures.Show(_pictures.Settings.Stored);
        int unchanged = changes;
        Pictures.Show(changed);
        PreviewLevel? skyrim = Pictures.OwnLevelOf(PreviewScreen.Library, Skyrim);
        Pictures.Show(new AppSettings());

        Assert.Equal((0, PreviewLevel.Cards, 2), (unchanged, skyrim, changes));
        Assert.Null(Pictures.OwnLevelOf(PreviewScreen.Browse, Stardew));
    }

    [Fact]
    public async Task ShowsTheScreensPreviewsTheSettingsScreenReadsTellingTheScreensOnlyOfAChange()
    {
        await Pictures.LoadAsync();
        int changes = 0;
        Pictures.Changed += (_, _) => changes++;
        AppSettings stored = _pictures.Settings.Stored;

        // The defaults, stored, are no change.
        Pictures.Show(stored with { Previews = new Dictionary<PreviewScreen, PreviewLevel> { [PreviewScreen.Browse] = PreviewLevel.Cards } });
        int unchanged = changes;
        Pictures.Show(stored with { Previews = new Dictionary<PreviewScreen, PreviewLevel> { [PreviewScreen.Downloads] = PreviewLevel.Thumbnails } });
        (PreviewLevel, PreviewLevel) shown = (Pictures.LevelOf(PreviewScreen.Downloads), Pictures.LevelOf(PreviewScreen.Library));
        Pictures.Show(stored);

        Assert.Equal((0, (PreviewLevel.Thumbnails, PreviewLevel.Thumbnails), 2), (unchanged, shown, changes));
        Assert.Equal(PreviewLevel.Off, Pictures.LevelOf(PreviewScreen.Downloads));
    }

    [Fact]
    public async Task AGameWithAPreviewsOfItsOwnTheSameAsTheScreensIsStillItsOwn()
    {
        _pictures.Settings.Stored = new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer)
                .Add(Stardew, new GameSettings { Previews = PreviewLevel.Thumbnails }),
        };
        PreviewsViewModel library = await StartedAsync(PreviewScreen.Library);
        library.ShowFor(Stardew);

        library.IsOff = true;
        await Pictures.SavedAsync();

        Assert.True(library.IsGamesOwn);
        Assert.Equal(["game nexus/stardewvalley"], _pictures.Settings.Changes);
        Assert.Equal(PreviewLevel.Thumbnails, Pictures.LevelOf(PreviewScreen.Library));
    }

    private async Task<PreviewsViewModel> StartedAsync(PreviewScreen screen)
    {
        PreviewsViewModel previews = new(screen, Pictures);
        await Pictures.LoadAsync();
        previews.Refresh();
        return previews;
    }
}
