using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Services;

/// <summary>
/// Opening a game's own settings: the Settings screen is told which game, then shown.
/// </summary>
public sealed class GameSettingsOpenerTests
{
    [Fact]
    public void TellsTheGameThenShowsTheSettings()
    {
        NavigationService navigation = new();
        GameSettingsOpener opener = new(navigation);
        List<string> happened = [];
        opener.Opening += (_, game) => happened.Add($"opening {game.Key} on {navigation.Current}");
        navigation.Navigated += (_, screen) => happened.Add($"showing {screen}");

        opener.Open(TestData.Mod("1", "stardewvalley").Game);

        Assert.Equal(["opening stardewvalley on Library", "showing Settings"], happened);
    }

    [Fact]
    public void RefusesNoGameOrNoNavigation()
    {
        Assert.Throws<ArgumentNullException>(() => new GameSettingsOpener(null!));
        Assert.Throws<ArgumentNullException>(() => new GameSettingsOpener(new NavigationService()).Open(null!));
    }
}
