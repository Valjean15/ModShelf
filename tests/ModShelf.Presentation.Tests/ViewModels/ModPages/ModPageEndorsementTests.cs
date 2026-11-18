using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Presentation.Tests.ViewModels.ModPages;

/// <summary>
/// Endorsing a mod from its page, on a site that lets ModShelf endorse: whether the account endorsed it, read once a
/// site; endorsing with the version the page shows, abstaining, and the site's refusals in its words.
/// </summary>
public sealed class ModPageEndorsementTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ASiteThatDoesntEndorseOffersNothing()
    {
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal((false, false), (mod.CanEndorse, mod.ToggleEndorseCommand.CanExecute(null)));
        Assert.Empty(_harness.Endorsements.Read);
    }

    [Fact]
    public async Task ShowsWhatTheAccountSaidReadingTheSitesListOnce()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.Said[SkyUi] = Endorsement.Endorsed;
        await _harness.StartAsync();

        ModPageViewModel first = await OpenAsync();
        ModPageViewModel again = await OpenAsync();

        Assert.Equal((true, true, "Endorse"), (first.CanEndorse, first.IsEndorsed, first.EndorseText));
        Assert.True(again.IsEndorsed);
        Assert.Equal([TestData.Nexus], _harness.Endorsements.Read);
    }

    [Fact]
    public async Task EndorsesWithThePagesVersionThenAbstains()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync();

        await mod.ToggleEndorseCommand.ExecuteAsync(null);
        (bool, string?) endorsed = (mod.IsEndorsed, mod.Note);
        await mod.ToggleEndorseCommand.ExecuteAsync(null);

        Assert.Equal((true, "Nexus Mods has your endorsement."), endorsed);
        Assert.Equal((false, "You chose to abstain on Nexus Mods.", false), (mod.IsEndorsed, mod.Note, mod.IsNoteAProblem));
        Assert.Equal([(SkyUi, Endorsement.Endorsed, "1.2"), (SkyUi, Endorsement.Abstained, "1.2")], _harness.Endorsements.Given);
    }

    [Fact]
    public async Task ARefusalIsSaidInTheSitesWords()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.Refusals[SkyUi] = new HttpRequestException("Nexus Mods answered 403. NOT_DOWNLOADED_MOD", null, HttpStatusCode.Forbidden);
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync();

        await mod.ToggleEndorseCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods wouldn't let you endorse the mod: Nexus Mods answered 403. NOT_DOWNLOADED_MOD", mod.Note);
        Assert.Equal((true, false), (mod.IsNoteAProblem, mod.IsEndorsed));
    }

    [Fact]
    public async Task AListThatCantBeReadIsLoggedAndEndorsingIsStillOffered()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.ListFailure = new AuthenticationFailedException("No account is linked.");
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal((true, false), (mod.ToggleEndorseCommand.CanExecute(null), mod.IsEndorsed));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Information: Couldn't read the endorsements on nexus", StringComparison.Ordinal));
    }

    private Task<ModPageViewModel> OpenAsync() => _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
}
