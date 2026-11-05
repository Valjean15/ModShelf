using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Each game in the Games category, and the chosen game's page, show its site once two sites are in use, as the screen
/// finds each time it's shown. While they do, the same game on two sites goes by its own name beside its site's chip.
/// </summary>
public sealed class SettingsSitesTests
{
    private static readonly GameRef NexusStardew = TestData.Mod("2400", "stardewvalley").Game;
    private static readonly GameRef ExampleStardew = ExampleSite.StardewValley.Game;

    private readonly SettingsHarness _harness = new(example: new ExampleSite());

    public SettingsSitesTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
        _harness.Catalog.GameNames["skyrimspecialedition"] = "Skyrim Special Edition";
        _harness.Library.Add(
            TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"),
            TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"),
            TestData.LibraryMod(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), "Better Crops"));
    }

    private SettingsViewModel Screen => _harness.Screen;

    private GameSettingsViewModel Game => Assert.IsType<GameSettingsViewModel>(Screen.Game);

    [Fact]
    public async Task WhileOneSiteIsInUseNoGameShowsItsSite()
    {
        await StartAsync(several: false);

        Assert.False(Screen.ShowSites);
        Assert.Equal(
            [
                ("Skyrim Special Edition", false, "Nexus Mods"),
                ("Stardew Valley (Example Mods)", false, "Example Mods"),
                ("Stardew Valley (Nexus Mods)", false, "Nexus Mods"),
            ],
            Rows());
        Choose(NexusStardew);
        Assert.Equal(("Stardew Valley (Nexus Mods)", false, "Nexus Mods"), (Game.Name, Game.ShowSite, Game.SiteName));
        Assert.Equal(1, _harness.Sites.Asked);
    }

    [Fact]
    public async Task OnceTwoSitesAreInUseEachGameShowsItsSiteAndGoesByItsOwnName()
    {
        await StartAsync(several: true);

        Assert.True(Screen.ShowSites);
        Assert.Equal(
            [
                ("Skyrim Special Edition", true, "Nexus Mods"),
                ("Stardew Valley", true, "Example Mods"),
                ("Stardew Valley", true, "Nexus Mods"),
            ],
            Rows());
    }

    [Fact]
    public async Task TheChosenGamesPageShowsItsSiteAndStillNamesTheOtherWithItsSite()
    {
        await StartAsync(several: true);

        Choose(NexusStardew);

        Assert.Equal(("Stardew Valley", true, "Nexus Mods"), (Game.Name, Game.ShowSite, Game.SiteName));
        Assert.Equal("Copy settings from Stardew Valley (Example Mods)", Assert.Single(Game.CopyChoices).Text);

        Choose(ExampleStardew);

        Assert.Equal(("Stardew Valley", true, "Example Mods"), (Game.Name, Game.ShowSite, Game.SiteName));
        Assert.Equal("Copy settings from Stardew Valley (Nexus Mods)", Assert.Single(Game.CopyChoices).Text);
    }

    [Fact]
    public async Task SitesGoOnceOneIsInUseAgain()
    {
        await StartAsync(several: true);
        Choose(ExampleStardew);

        await ShowAsync(several: false);

        Assert.Equal(2, _harness.Sites.Asked);
        Assert.False(Screen.ShowSites);
        Assert.Equal(
            [
                ("Skyrim Special Edition", false, "Nexus Mods"),
                ("Stardew Valley (Example Mods)", false, "Example Mods"),
                ("Stardew Valley (Nexus Mods)", false, "Nexus Mods"),
            ],
            Rows());
        Assert.Equal(("Stardew Valley (Example Mods)", false), (Game.Name, Game.ShowSite));
        Assert.Equal(ExampleStardew, Screen.SelectedGame?.Game);
    }

    private (string Name, bool ShowSite, string SiteName)[] Rows()
        => [.. Screen.Games.Select(row => (row.Name, row.ShowSite, row.SiteName)).OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.SiteName, StringComparer.Ordinal)];

    private void Choose(GameRef game) => Screen.SelectedGame = Screen.Games.Single(row => row.Game == game);

    private async Task StartAsync(bool several)
    {
        await _harness.StartAsync();
        await ShowAsync(several);
    }

    /// <summary>
    /// Shows the screen, as the main window does, with two sites in use or not, and waits until it has read the games.
    /// </summary>
    private async Task ShowAsync(bool several)
    {
        _harness.Sites.Several = several;
        Screen.OnShown();
        await Screen.GamesWork;
        await Screen.SitesWork;
    }
}
