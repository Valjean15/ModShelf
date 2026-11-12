using ModShelf.Application.Installed.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Settings › Games' games on this PC: each installed game with its games on the sites, and the dialog choosing them.
/// </summary>
public sealed class InstalledGamesViewModelTests
{
    private static readonly SourceId Banana = new("gamebanana");
    private static readonly GameInfo Stardew = new(new GameRef(TestData.Nexus, "stardewvalley"), "Stardew Valley");
    private static readonly GameInfo Skyrim = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "Skyrim Special Edition");
    private static readonly GameInfo Royal = new(new GameRef(Banana, "16951"), "Persona 5 Royal (PC)");

    private readonly ScriptedInstalledGames _installed = new();
    private readonly ScriptedLookup _lookup = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly TestPictures _pictures = new();
    private readonly RecordingLogger<InstalledGamesViewModel> _logger = new();
    private readonly InstalledGamesViewModel _card;
    private readonly InstalledGame _stardew;
    private readonly InstalledGame _elderScrolls;
    private readonly InstalledGame _fallout;

    public InstalledGamesViewModelTests()
    {
        _lookup.Games[TestData.Nexus] = [Stardew, Skyrim];
        _stardew = _installed.Add("413150", "Stardew Valley");
        _elderScrolls = _installed.Add("489830", "The Elder Scrolls V: Skyrim Special Edition");
        _fallout = _installed.Add("377160", "Fallout 4");
        _installed.Pick(_fallout, TestData.Nexus, null);
        FakeSourceRegistry sources = new(new ScriptedCatalog(TestData.Nexus, "Nexus Mods"), new FakeSource(Banana, "GameBanana"));
        _card = new InstalledGamesViewModel(_installed, _lookup, sources, _pictures.Pictures, _dialogs, _logger);
    }

    [Fact]
    public async Task ListsEachInstalledGameWithItsGamesOnTheSites()
    {
        _installed.Pick(_stardew, Banana, Royal);

        await ReadAsync();

        Assert.Equal(
            [
                ("Stardew Valley", "Nexus Mods: Stardew Valley · GameBanana: Persona 5 Royal (PC), chosen", true),
                ("The Elder Scrolls V: Skyrim Special Edition", "No site's game yet", false),
                ("Fallout 4", "Nexus Mods: none, chosen", false),
            ],
            _card.Games.Select(row => (row.Name, row.MatchesText, row.HasGame)));
        Assert.Equal("Steam: 3 games installed, 1 with a site's game.", _card.StatusText);
        Assert.Equal("Choose Stardew Valley's games", _card.Games[0].ChooseText);
        Assert.True(_card.HasGames);
        Assert.Null(_card.Problem);
        Assert.Equal([[], [Stardew, Skyrim]], _installed.Matched);
    }

    [Fact]
    public async Task SaysWhenASitesGamesShareTheInstalledGamesName()
    {
        _lookup.Games[TestData.Nexus] = [Stardew, new GameInfo(new GameRef(TestData.Nexus, "stardewvalleyexpanded"), "Stardew Valley")];

        await ReadAsync();

        Assert.Equal("Nexus Mods: 2 games of this name", _card.Games[0].MatchesText);
    }

    [Fact]
    public async Task WithoutSteamThereAreNoGamesToMatch()
    {
        _installed.HasSteam = false;

        await ReadAsync();

        Assert.Equal("Steam isn't on this computer, so there are no installed games to match.", _card.StatusText);
        Assert.Empty(_card.Games);
        Assert.False(_card.HasGames);
    }

    [Theory]
    [InlineData(false, "Nexus Mods couldn't list its games, so they match only as you choose them: Nexus Mods is down.")]
    [InlineData(true, "Nexus Mods needs your account linked, or refused the one linked. Link it in Settings › Accounts, then try again.")]
    public async Task ASiteThatCantListItsGamesIsSaidAndItsGamesMatchOnlyAsChosen(bool account, string problem)
    {
        _lookup.Refusals["GetGamesAsync"] = account ? new AuthenticationFailedException("No account.") : new HttpRequestException("Nexus Mods is down.");

        await ReadAsync();

        Assert.Equal(problem, _card.Problem);
        Assert.Equal(["No site's game yet", "No site's game yet", "Nexus Mods: none, chosen"], _card.Games.Select(row => row.MatchesText));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task ASlowSiteKeepsNoneOfTheOthersWaitingAndProblemsAreSaidInTheSitesOrder()
    {
        SourceId modio = new("modio");
        GameInfo modioStardew = new(new GameRef(modio, "stardew-valley"), "Stardew Valley");
        _lookup.Games[modio] = [modioStardew];
        TaskCompletionSource nexus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _lookup.HeldGames[TestData.Nexus] = nexus;
        InstalledGamesViewModel card = new(
            _installed,
            _lookup,
            new FakeSourceRegistry(new ScriptedCatalog(TestData.Nexus, "Nexus Mods"), new ScriptedCatalog(modio, "mod.io")),
            _pictures.Pictures,
            _dialogs,
            _logger);

        card.OnShown();
        string whileNexusReads = card.Games[0].MatchesText;
        (bool Reading, string? Status) before = (card.IsReading, card.StatusText);
        nexus.SetResult();
        await card.Reading;

        Assert.Equal("mod.io: Stardew Valley", whileNexusReads);
        Assert.Equal((true, null), before);
        Assert.Equal("Nexus Mods: Stardew Valley · mod.io: Stardew Valley", card.Games[0].MatchesText);
        Assert.Equal([[], [modioStardew], [Stardew, Skyrim, modioStardew]], _installed.Matched);
    }

    [Fact]
    public async Task ProblemsAreSaidInTheSitesOrderHoweverTheyAnswer()
    {
        SourceId modio = new("modio");
        TaskCompletionSource nexus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _lookup.HeldGames[TestData.Nexus] = nexus;
        _lookup.Refusals["GetGamesAsync"] = new HttpRequestException("It's down.");
        InstalledGamesViewModel card = new(
            _installed,
            _lookup,
            new FakeSourceRegistry(new ScriptedCatalog(TestData.Nexus, "Nexus Mods"), new ScriptedCatalog(modio, "mod.io")),
            _pictures.Pictures,
            _dialogs,
            _logger);

        card.OnShown();
        _lookup.Refusals["GetGamesAsync"] = new HttpRequestException("It's slow.");
        nexus.SetResult();
        await card.Reading;

        Assert.Equal(
            "Nexus Mods couldn't list its games, so they match only as you choose them: It's slow. "
            + "mod.io couldn't list its games, so they match only as you choose them: It's down.",
            card.Problem);
    }

    [Fact]
    public async Task ASiteThatCantListItsGamesWhenReadAgainMatchesOnlyAsChosen()
    {
        await ReadAsync();
        string before = _card.Games[0].MatchesText;
        _lookup.Refusals["GetGamesAsync"] = new HttpRequestException("Nexus Mods is down.");

        await ReadAsync();

        Assert.Equal("Nexus Mods: Stardew Valley", before);
        Assert.Equal(["No site's game yet", "No site's game yet", "Nexus Mods: none, chosen"], _card.Games.Select(row => row.MatchesText));
        Assert.Equal([[], [Stardew, Skyrim], [Stardew, Skyrim], []], _installed.Matched);
    }

    [Fact]
    public async Task InstalledGamesThatCantBeReadAreSaidAndLogged()
    {
        _installed.Failure = new IOException("Steam's folder can't be read.");

        await ReadAsync();

        Assert.Equal("Something went wrong. The log has the details.", _card.Problem);
        Assert.False(_card.IsReading);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: Reading the games installed on this computer failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGameChosenInTheDialogIsKeptAndTheGamesReadAgain()
    {
        await ReadAsync();
        InstalledGameRowViewModel row = _card.Games[1];
        bool closed = false;
        int offered = 0;
        _dialogs.InPickInstalled = async picking =>
        {
            picking.CloseRequested += (_, _) => closed = true;
            offered = picking.Picker.GameCount;
            picking.Picker.Query = "skyrim";
            picking.Picker.ChooseCommand.Execute(null);
            await picking.Changing;
        };

        await row.ChooseCommand.ExecuteAsync(null);

        Assert.Equal(Skyrim.Game, _installed.Picks[_elderScrolls.Key][TestData.Nexus]?.Game);
        Assert.Equal((true, 2), (closed, offered));
        Assert.Same(row, _card.Games[1]);
        Assert.Equal("Nexus Mods: Skyrim Special Edition, chosen", row.MatchesText);
        Assert.Equal([[], [Stardew, Skyrim], [Stardew, Skyrim], [Stardew, Skyrim]], _installed.Matched);
    }

    [Fact]
    public async Task NoneHereThenMatchByNameInTheDialog()
    {
        await ReadAsync();
        InstalledGameRowViewModel row = _card.Games[0];
        List<(string Message, string Site, string Text, bool CanSayNone, bool CanMatchByName)> shown = [];
        _dialogs.InPickInstalled = async picking =>
        {
            InstalledSiteViewModel site = Assert.Single(picking.Sites);
            shown.Add((picking.Message, site.SiteName, site.MatchText, site.CanSayNone, site.CanMatchByName));
            (site.CanSayNone ? site.NoneCommand : site.ByNameCommand).Execute(null);
            await picking.Changing;
        };

        await row.ChooseCommand.ExecuteAsync(null);
        string afterNone = row.MatchesText;
        await row.ChooseCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                ("Stardew Valley: choose its game on a site, below, or say a site has none.", "Nexus Mods", "Stardew Valley, by its name", true, false),
                ("Stardew Valley: choose its game on a site, below, or say a site has none.", "Nexus Mods", "None, chosen", false, true),
            ],
            shown);
        Assert.Equal("Nexus Mods: none, chosen", afterNone);
        Assert.Equal("Nexus Mods: Stardew Valley", row.MatchesText);
        Assert.Empty(_installed.Picks[_stardew.Key]);
    }

    [Fact]
    public async Task TheDialogFindsTheGamesOfASiteThatFindsThemByName()
    {
        _lookup.ByName.Add(Banana);
        _lookup.Games[Banana] = [Royal];
        InstalledGamesViewModel card = new(
            _installed,
            _lookup,
            new FakeSourceRegistry(new ScriptedCatalog(TestData.Nexus, "Nexus Mods"), new ScriptedCatalog(Banana, "GameBanana")),
            _pictures.Pictures,
            _dialogs,
            _logger);
        card.OnShown();
        await card.Reading;
        _dialogs.InPickInstalled = async picking =>
        {
            picking.Picker.Query = "persona";
            await picking.Picker.Finding;
            picking.Picker.ChooseCommand.Execute(null);
            await picking.Changing;
        };

        await card.Games[1].ChooseCommand.ExecuteAsync(null);

        Assert.Equal(Royal.Game, _installed.Picks[_elderScrolls.Key][Banana]?.Game);
        Assert.DoesNotContain($"GetGamesAsync {Banana}", _lookup.Asked);
    }

    [Fact]
    public async Task ASiteWithAGameChosenCanSayNoneOrMatchByName()
    {
        _installed.Pick(_elderScrolls, TestData.Nexus, Skyrim);
        await ReadAsync();
        (string Text, bool CanSayNone, bool CanMatchByName)? shown = null;
        _dialogs.InPickInstalled = picking =>
        {
            InstalledSiteViewModel site = Assert.Single(picking.Sites);
            shown = (site.MatchText, site.CanSayNone, site.CanMatchByName);
            return Task.CompletedTask;
        };

        await _card.Games[1].ChooseCommand.ExecuteAsync(null);

        Assert.Equal(("Skyrim Special Edition, chosen", true, true), shown);
    }

    [Fact]
    public async Task TheDialogListsOnlyTheSitesWithAGameOrAChoice()
    {
        await ReadAsync();
        List<int> sites = [];
        _dialogs.InPickInstalled = picking =>
        {
            sites.Add(picking.Sites.Count);
            sites.Add(picking.HasSites ? 1 : 0);
            return Task.CompletedTask;
        };

        await _card.Games[1].ChooseCommand.ExecuteAsync(null);
        await _card.Games[2].ChooseCommand.ExecuteAsync(null);

        Assert.Equal([0, 0, 1, 1], sites);
    }

    [Fact]
    public async Task AChoiceThatCantBeKeptIsSaidAndTheDialogStaysOpen()
    {
        await ReadAsync();
        _installed.PickFailure = new IOException("The disk is full.");
        bool closed = false;
        string? problem = null;
        _dialogs.InPickInstalled = async picking =>
        {
            picking.CloseRequested += (_, _) => closed = true;
            picking.Sites[0].NoneCommand.Execute(null);
            await picking.Changing;
            problem = picking.Problem;
        };

        await _card.Games[0].ChooseCommand.ExecuteAsync(null);

        Assert.Equal(("Couldn't keep the choice: The disk is full.", false), (problem, closed));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't keep the game chosen for an installed game", StringComparison.Ordinal));
    }

    private async Task ReadAsync()
    {
        _card.OnShown();
        await _card.Reading;
    }
}
