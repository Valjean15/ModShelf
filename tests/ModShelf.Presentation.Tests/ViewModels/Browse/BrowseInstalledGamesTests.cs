using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// Browse's games on this PC: the installed games' games first in the game picker, once the sites' games are read and
/// each time Browse shows again.
/// </summary>
public sealed class BrowseInstalledGamesTests : IDisposable
{
    private readonly BrowseHarness _harness = new();

    public BrowseInstalledGamesTests()
    {
        _harness.Installed.Add("413150", "Stardew Valley");
        _harness.Installed.Add("377160", "Fallout 4");
        _harness.Installed.Add("1", "Not On A Site");
    }

    private BrowseViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OffersTheInstalledGamesGamesOnceTheSitesGamesAreRead()
    {
        await ShowAsync();

        Assert.Equal(
            [("On this PC (Steam)", "Fallout 4"), (null, "Stardew Valley")],
            _harness.Screen.Picker.Entries.Select(entry => (entry.Heading, entry.Name)));
        Assert.Equal(
            [BrowseHarness.Stardew.Game, BrowseHarness.Skyrim.Game, BrowseHarness.Fallout.Game],
            Assert.Single(_harness.Installed.Matched).Select(game => game.Game));
    }

    [Fact]
    public async Task LeavesOutTheGamesTheSettingsHide()
    {
        await ShowAsync();

        _harness.Hidden.Show(new AppSettings
        {
            Games = ImmutableDictionary.Create<GameRef, GameSettings>(AppSettings.GameComparer)
                .Add(BrowseHarness.Fallout.Game, new GameSettings { Hidden = true }),
        });
        _harness.UiThread.RunPending();

        Assert.Equal(["Stardew Valley"], Screen.Picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task ChoosingOneOfTheGamesANameMatchesRemembersItAndMatchesAgain()
    {
        GameInfo expanded = new(new GameRef(TestData.Nexus, "stardewvalleyexpanded"), "Stardew Valley");
        _harness.Lookup.Games[TestData.Nexus] = [.. _harness.Lookup.Games[TestData.Nexus], expanded];
        await ShowAsync();
        int shared = Screen.Picker.Entries.Count(entry => entry.Name == "Stardew Valley");

        Entry(expanded.Game).PickCommand.Execute(null);
        await Screen.InstalledWork;
        await Screen.Listing;

        Assert.Equal(2, shared);
        Assert.Equal(expanded.Game, _harness.Installed.Picks["steam/413150"][TestData.Nexus]?.Game);
        Assert.Equal(["Fallout 4", "Stardew Valley"], Screen.Picker.Entries.Select(entry => entry.Name));
        Assert.Equal(expanded.Game, Screen.Picker.Entries[1].Game);
        Assert.Equal(expanded.Game, Screen.Game?.Game);
        Assert.Equal(2, _harness.Installed.Matched.Count);
    }

    [Fact]
    public async Task MatchesTheInstalledGamesAgainEachTimeBrowseShows()
    {
        await ShowAsync();
        _harness.Installed.Add("489830", "Skyrim Special Edition");

        _harness.Navigation.NavigateTo(Presentation.Services.Screen.Library);
        await ShowAsync();

        Assert.Equal(2, _harness.Installed.Matched.Count);
        Assert.Equal(["Fallout 4", "Skyrim Special Edition", "Stardew Valley"], Screen.Picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task InstalledGamesThatCantBeReadAreLoggedAndTheGamesOfferedStill()
    {
        _harness.Installed.Failure = new IOException("Steam's folder can't be read.");

        await ShowAsync();
        Screen.Picker.Query = "fallout";

        Assert.Equal([("Games on Nexus Mods", "Fallout 4")], Screen.Picker.Entries.Select(entry => (entry.Heading, entry.Name)));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Reading the games installed on this computer failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChoiceThatCantBeKeptIsLoggedAndTheGamesMatchAgain()
    {
        GameInfo expanded = new(new GameRef(TestData.Nexus, "stardewvalleyexpanded"), "Stardew Valley");
        _harness.Lookup.Games[TestData.Nexus] = [.. _harness.Lookup.Games[TestData.Nexus], expanded];
        await ShowAsync();
        _harness.Installed.PickFailure = new IOException("The disk is full.");

        Entry(expanded.Game).PickCommand.Execute(null);
        await Screen.InstalledWork;

        Assert.Empty(_harness.Installed.Picks);
        Assert.Equal(2, _harness.Installed.Matched.Count);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Warning: Couldn't remember the game of nexus chosen for an installed game", StringComparison.Ordinal));
    }

    private GamePickerEntryViewModel Entry(GameRef game) => Screen.Picker.Entries.Single(entry => entry.Game == game);

    private async Task ShowAsync()
    {
        if (_harness.Work.IsCompleted)
        {
            await _harness.StartAsync();
        }

        await _harness.ShowAsync();
        await Screen.InstalledWork;
    }
}
