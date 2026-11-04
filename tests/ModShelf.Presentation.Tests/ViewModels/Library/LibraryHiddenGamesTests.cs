using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The games the settings hide, left out of the Library's games until asked for; and a game's gear, which opens its own
/// settings.
/// </summary>
public sealed class LibraryHiddenGamesTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly ModRef Unofficial = TestData.Mod("1", "fallout4");

    private readonly LibraryHarness _harness = new();

    public LibraryHiddenGamesTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameNames["fallout4"] = "Fallout 4";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task HiddenGamesAreLeftOutUntilAskedFor()
    {
        _harness.Settings.Stored = Hiding("StardewValley", "Fallout4");

        await StartAsync();

        Assert.Equal(["skyrimspecialedition"], Screen.Games.Select(game => game.Game.Key));
        Assert.Equal((true, "Show hidden games (2)"), (Screen.HasHiddenGames, Screen.ShowHiddenGamesText));
        Assert.Equal("3 games · 3 mods · 0 B on disk", Screen.SummaryText);

        Screen.ShowHiddenGames = true;

        Assert.Equal(["fallout4", "skyrimspecialedition", "stardewvalley"], Screen.Games.Select(game => game.Game.Key).Order(StringComparer.Ordinal));

        Screen.ShowHiddenGames = false;

        Assert.Equal(["skyrimspecialedition"], Screen.Games.Select(game => game.Game.Key));
    }

    [Fact]
    public async Task WithNoGameHiddenTheToggleDoesntShow()
    {
        _harness.Settings.Stored = Hiding("morrowind");

        await StartAsync();

        Assert.Equal(3, Screen.Games.Count);
        Assert.False(Screen.HasHiddenGames);
    }

    [Fact]
    public async Task HidingAGameOnTheSettingsScreenHidesItAtOnce()
    {
        await StartAsync();

        _harness.Hidden.Show(Hiding("stardewvalley"));

        Assert.Equal(2, Screen.Games.Count);
        Assert.DoesNotContain(Screen.Games, game => game.Game == Smapi.Game);
        Assert.Equal("Show hidden games (1)", Screen.ShowHiddenGamesText);

        _harness.Hidden.Show(new AppSettings());

        Assert.Equal(3, Screen.Games.Count);
        Assert.False(Screen.HasHiddenGames);
    }

    [Fact]
    public async Task SettingsThatCantBeReadHideNoGame()
    {
        _harness.Settings.ReadRefusal = new InvalidDataException("Not JSON.");

        await StartAsync();

        Assert.Equal(3, Screen.Games.Count);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AGamesGearOpensItsOwnSettings()
    {
        await StartAsync();
        _harness.Game(Smapi.Game).OpenCommand.Execute(null);

        Screen.OpenGameSettingsCommand.Execute(null);

        Assert.Equal([Smapi.Game], _harness.GameSettings.Opened);
    }

    [Fact]
    public async Task TheGearDoesNothingWithNoGameOpen()
    {
        await StartAsync();

        Screen.OpenGameSettingsCommand.Execute(null);

        Assert.Empty(_harness.GameSettings.Opened);
    }

    /// <summary>
    /// Nexus Mods' games, by their keys in any case, hidden.
    /// </summary>
    private static AppSettings Hiding(params string[] games)
        => new() { Games = games.ToDictionary(game => new GameRef(TestData.Nexus, game), _ => new GameSettings { Hidden = true }, AppSettings.GameComparer) };

    private Task StartAsync()
        => _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"), TestData.LibraryMod(Unofficial, "Unofficial Patch"));
}
