using System.Net;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Endorsements in the Library, on a site that lets ModShelf endorse: which mods the account endorsed, read once when a
/// game of the site opens; the toggle that shows those it hasn't; and Endorse for the selected mods, which says which
/// first, and what the site refused.
/// </summary>
public sealed class LibraryEndorsementTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("32444");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ASiteThatDoesntEndorseOffersNothing()
    {
        await StartAsync();

        Row(SkyUi).IsSelected = true;

        Assert.Equal((false, false, (bool?)null), (Screen.ShowsEndorseSelected, Screen.EndorseSelectedCommand.CanExecute(null), Row(SkyUi).IsEndorsed));
        Assert.False(Toggle.IsShown);
        Assert.Empty(_harness.Endorsements.Read);
    }

    [Fact]
    public async Task ReadsWhichModsAreEndorsedOnceAndShowsThoseThatArent()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.Said[SkyUi] = Endorsement.Endorsed;
        await StartAsync();

        (bool?, bool?) endorsed = (Row(SkyUi).IsEndorsed, Row(Address).IsEndorsed);
        Toggle.IsOn = true;

        Assert.Equal((true, false), endorsed);
        Assert.Equal((true, 1), (Toggle.IsShown, Toggle.Count));
        Assert.Equal(["Address Library"], Screen.GameMods.Select(mod => mod.Name));
        Assert.Equal([TestData.Nexus], _harness.Endorsements.Read);
    }

    [Fact]
    public async Task EndorsesTheSelectedModsNotEndorsedAfterSayingWhich()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.Said[SkyUi] = Endorsement.Endorsed;
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        string text = Screen.EndorseSelectedText;
        _harness.Dialogs.Confirm.Enqueue(true);

        await Screen.EndorseSelectedCommand.ExecuteAsync(null);

        Assert.Equal("Endorse (1)", text);
        Assert.Equal([("Endorse this mod on Nexus Mods?", "Address Library", "Endorse")], _harness.Dialogs.Confirmations);
        Assert.Equal([(Address, Endorsement.Endorsed, (string?)null)], _harness.Endorsements.Given);
        Assert.True(Row(Address).IsEndorsed);
        Assert.Equal(("Endorse (0)", false), (Screen.EndorseSelectedText, Screen.EndorseSelectedCommand.CanExecute(null)));
        Assert.False(Row(Address).IsSelected);
    }

    [Fact]
    public async Task SaysWhatTheSiteRefusedAndKeepsThoseSelected()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        _harness.Endorsements.Refusals[Address] = new HttpRequestException("Nexus Mods answered 403. NOT_DOWNLOADED_MOD", null, HttpStatusCode.Forbidden);
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        _harness.Dialogs.Confirm.Enqueue(true);

        await Screen.EndorseSelectedCommand.ExecuteAsync(null);

        Assert.Equal("Endorse these 2 mods on Nexus Mods?", Assert.Single(_harness.Dialogs.Confirmations).Title);
        (string Title, string Message) said = Assert.Single(_harness.Dialogs.Messages);
        Assert.Equal(("Some mods weren't endorsed", "Address Library: Nexus Mods refused: Nexus Mods answered 403. NOT_DOWNLOADED_MOD"), said);
        Assert.Equal((false, true), (Row(SkyUi).IsSelected, Row(Address).IsSelected));
    }

    [Fact]
    public async Task NothingIsEndorsedWhenTheUserSaysNo()
    {
        _harness.Endorsements.Sources.Add(TestData.Nexus);
        await StartAsync();
        Row(SkyUi).IsSelected = true;

        await Screen.EndorseSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Endorsements.Given);
        Assert.True(Row(SkyUi).IsSelected);
    }

    private ModFilterViewModel Toggle => Screen.ModFilters.Single(filter => filter.Kind == ModFilterKind.NotEndorsed);

    private LibraryModViewModel Row(ModRef mod) => _harness.Row(TestData.LibraryMod(mod, "any"));

    private async Task StartAsync()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"));
        _ = Row(SkyUi);
        await Screen.Checking;
    }
}
