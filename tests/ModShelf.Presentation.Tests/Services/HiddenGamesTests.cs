using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Services;

/// <summary>
/// The hidden games, as the screens last read them from the settings.
/// </summary>
public sealed class HiddenGamesTests
{
    private readonly HiddenGames _hidden = new();
    private int _changes;

    public HiddenGamesTests()
    {
        _hidden.Changed += (_, _) => _changes++;
    }

    [Fact]
    public void HidesNoGameAtFirst()
    {
        Assert.Equal((0, false), (_hidden.Count, _hidden.IsHidden(Nexus("stardewvalley"))));
    }

    [Fact]
    public void TakesTheGamesTheSettingsHideInAnyCase()
    {
        _hidden.Show(Settings(("StardewValley", true), ("fallout4", false), ("skyrimspecialedition", null)));

        Assert.Equal(
            (1, true, false, false),
            (_hidden.Count, _hidden.IsHidden(Nexus("stardewvalley")), _hidden.IsHidden(Nexus("fallout4")), _hidden.IsHidden(Nexus("skyrimspecialedition"))));
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void SaysWhenTheyChangeOnly()
    {
        _hidden.Show(Settings(("stardewvalley", true)));
        _hidden.Show(Settings(("STARDEWVALLEY", true), ("fallout4", false)));
        _hidden.Show(new AppSettings());
        _hidden.Show(new AppSettings());

        Assert.Equal(2, _changes);
        Assert.False(_hidden.IsHidden(Nexus("stardewvalley")));
    }

    [Fact]
    public void HidingAGameHidesNotTheSameKeyOnAnotherSite()
    {
        _hidden.Show(Settings(("stardewvalley", true)));

        Assert.True(_hidden.IsHidden(Nexus("stardewvalley")));
        Assert.False(_hidden.IsHidden(new GameRef(new SourceId("example"), "stardewvalley")));
    }

    /// <summary>
    /// Nexus Mods' games' own settings, by their keys in any case.
    /// </summary>
    private static AppSettings Settings(params (string Game, bool? Hidden)[] games)
        => new() { Games = games.ToDictionary(game => Nexus(game.Game), game => new GameSettings { Hidden = game.Hidden, Notify = false }, AppSettings.GameComparer) };

    private static GameRef Nexus(string key) => new(TestData.Nexus, key);
}
