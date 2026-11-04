using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// A game's previews on its settings page: the screens' until they're overridden, then its own, which its page in the
/// Library and Browse show at once. The screen shows them again as stored each time it's shown.
/// </summary>
public sealed class SettingsGamePreviewsTests
{
    private static readonly GameRef Stardew = TestData.Mod("1", "stardewvalley").Game;

    private readonly SettingsHarness _harness = new();

    public SettingsGamePreviewsTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    private ModPictures Pictures => _harness.Pictures.Pictures;

    [Fact]
    public async Task AGameUsesTheScreensPreviewsUntilItHasItsOwn()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Off),
        };

        await StartAsync();

        Assert.False(Game.HasOwnPreviews);
        Assert.Equal("The screens' own: no pictures in the Library, cards in Browse.", Game.EveryGamesPreviews);
        Assert.Equal((PreviewLevel.Off, true, false, false), (Game.Previews, Game.IsPreviewsOff, Game.IsPreviewsThumbnails, Game.IsPreviewsCards));
        Assert.Equal("Uses the defaults", Screen.Games[0].Note);
        await Game.Saving;
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task PreviewsAreOverriddenChangedAndSetBack()
    {
        await StartAsync();
        int changes = 0;
        Pictures.Changed += (_, _) => changes++;

        await Game.OverridePreviewsCommand.ExecuteAsync(null);
        (bool, PreviewLevel?, PreviewLevel?, string?) overridden = (Game.HasOwnPreviews, Own().Previews, Pictures.OwnLevelOf(PreviewScreen.Library, Stardew), Screen.Games[0].Note);

        Game.IsPreviewsCards = true;
        await Game.Saving;
        (PreviewLevel?, PreviewLevel) cards = (Own().Previews, Pictures.LevelOf(PreviewScreen.Browse, Stardew));

        await Game.UseDefaultPreviewsCommand.ExecuteAsync(null);

        Assert.Equal((true, PreviewLevel.Thumbnails, PreviewLevel.Thumbnails, "1 setting overridden"), overridden);
        Assert.Equal((PreviewLevel.Cards, PreviewLevel.Cards), cards);
        Assert.Equal((false, PreviewLevel.Thumbnails), (Game.HasOwnPreviews, Game.Previews));
        Assert.Empty(_harness.Settings.Stored.Games);
        Assert.Null(Pictures.OwnLevelOf(PreviewScreen.Browse, Stardew));
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task OverriddenPreviewsStartAsTheLibrarysAre()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Library, PreviewLevel.Cards),
        };
        await StartAsync();

        await Game.OverridePreviewsCommand.ExecuteAsync(null);

        Assert.Equal((true, PreviewLevel.Cards, true), (Game.HasOwnPreviews, Own().Previews, Game.IsPreviewsCards));
    }

    [Fact]
    public async Task ShowingTheScreenAgainShowsPreviewsChosenElsewhere()
    {
        _harness.Settings.Stored = Stored(PreviewLevel.Off);
        await StartAsync();
        PreviewsViewModel library = new(PreviewScreen.Library, Pictures);
        library.ShowFor(Stardew);
        _harness.Settings.Hold = new TaskCompletionSource();

        library.IsCards = true;
        Screen.OnShown();
        bool before = Screen.ShownWork.IsCompleted;
        _harness.Settings.Hold.SetResult();
        await Screen.ShownWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(before);
        Assert.Equal((true, PreviewLevel.Cards), (Game.HasOwnPreviews, Game.Previews));
        Assert.Equal(PreviewLevel.Cards, Pictures.LevelOf(PreviewScreen.Library, Stardew));
    }

    [Fact]
    public async Task ShowingTheScreenAgainShowsAChangeMadeFromTheCommandLine()
    {
        await StartAsync();

        _harness.Settings.Stored = Stored(PreviewLevel.Off);
        Screen.OnShown();
        await Screen.ShownWork;

        Assert.Equal((true, PreviewLevel.Off, "1 setting overridden"), (Game.HasOwnPreviews, Game.Previews, Screen.Games[0].Note));
        Assert.Equal(PreviewLevel.Off, Pictures.LevelOf(PreviewScreen.Library, Stardew));
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task SettingsThatCantBeReadWhenTheScreenShowsAgainAreSaid()
    {
        await StartAsync();

        _harness.Settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");
        Screen.OnShown();
        await Screen.ShownWork;

        Assert.Equal("settings.json isn't JSON.", Screen.Problem);
    }

    private static AppSettings Stored(PreviewLevel previews)
        => new()
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer).Add(Stardew, new GameSettings { Previews = previews }),
        };

    private GameSettings Own() => _harness.Settings.Stored.GameFor(Game.Game);

    /// <summary>
    /// Starts the screen with Stardew Valley, of the library, chosen, and the pictures read, as the screens read them.
    /// </summary>
    private async Task StartAsync()
    {
        _harness.Library.Add(TestData.LibraryMod(TestData.Mod("1", "stardewvalley"), "A mod"));
        await Pictures.LoadAsync();
        await _harness.StartAsync();
        Screen.OnShown();
        await Screen.GamesWork;
        await Screen.ShownWork;
        Assert.Equal(Stardew, Game.Game);
    }
}
