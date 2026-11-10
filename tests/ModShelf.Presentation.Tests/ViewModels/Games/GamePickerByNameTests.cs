using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Games;

/// <summary>
/// The game picker with a site that finds its games by name, as GameBanana does: asked as the name is typed, its games
/// listed with the others', the last answer taking the earlier ones' place.
/// </summary>
public sealed class GamePickerByNameTests
{
    private static readonly SourceId Banana = new("gamebanana");
    private static readonly GameInfo Stardew = new(new GameRef(TestData.Nexus, "stardewvalley"), "Stardew Valley");
    private static readonly GameInfo Skyrim = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "Skyrim Special Edition");
    private static readonly GameInfo Royal = new(new GameRef(Banana, "16951"), "Persona 5 Royal (PC)");
    private static readonly GameInfo Persona = new(new GameRef(Banana, "7545"), "Persona 5");
    private static readonly GameInfo Zelda = new(new GameRef(Banana, "5866"), "The Legend of Zelda");

    private readonly TestPictures _pictures = new();
    private readonly List<GameRef> _chosen = [];
    private readonly RecordingLogger<GamePickerByNameTests> _logger = new();
    private readonly ScriptedLookup _lookup = new() { Searchable = { TestData.Nexus, Banana }, ByName = { Banana } };
    private readonly FakeSourceRegistry _sources = new(new FakeSource(TestData.Nexus, "Nexus Mods"), new FakeSource(Banana, "GameBanana"));

    public GamePickerByNameTests()
    {
        _lookup.Games[Banana] = [Royal, Persona, Zelda];
    }

    [Fact]
    public void SaysWhatItFindsOnEachSite()
    {
        GamePickerViewModel picker = Picker();
        GamePickerViewModel onlyByName = new(_pictures.Pictures, _sources, _chosen.Add, byName: new GamesByName(_lookup, [Banana], _logger));

        picker.ShowGames([Stardew, Skyrim]);
        onlyByName.OpenCommand.Execute(null);

        Assert.Equal("Type to find any of the 2 games on Nexus Mods, or a game on GameBanana by its name.", picker.Hint);
        Assert.Equal("Type to find a game on GameBanana by its name.", onlyByName.Hint);
        Assert.True(onlyByName.IsOpen);
    }

    [Fact]
    public async Task TypingFindsTheSitesGamesByNameBesideTheOthers()
    {
        GamePickerViewModel picker = Picker();
        picker.ShowGames([Stardew, Skyrim]);

        picker.Query = "s";
        await picker.Finding;
        picker.Query = "persona 5";
        await picker.Finding;

        Assert.Equal(
            [("Games on GameBanana and Nexus Mods", "Persona 5", "GameBanana"), (null, "Persona 5 Royal (PC)", "GameBanana")],
            picker.Entries.Select(entry => (entry.Heading, entry.Name, entry.SiteName)));
        Assert.All(picker.Entries, entry => Assert.True(entry.ShowSite));
        Assert.Null(picker.FindingText);
        Assert.Contains($"FindGamesAsync {Banana} persona 5", _lookup.Asked);
    }

    [Fact]
    public async Task AGameListedAndFoundByNameTooIsOfferedOnce()
    {
        GamePickerViewModel picker = Picker();
        picker.ShowGames([Stardew, Royal]);

        picker.Query = "persona 5";
        await picker.Finding;

        Assert.Equal(["Persona 5", "Persona 5 Royal (PC)"], picker.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task SaysItsFindingUntilTheSiteAnswersAndOnlyThenThatNothingWasFound()
    {
        GamePickerViewModel picker = Picker();
        picker.ShowGames([Stardew]);
        TaskCompletionSource answer = new();
        _lookup.HeldFinds.Enqueue(answer);

        picker.Query = "qqzx";
        string? finding = picker.FindingText;
        string? nothingWhileFinding = picker.NothingFound;
        answer.SetResult();
        await picker.Finding;

        Assert.Equal("Finding games on GameBanana…", finding);
        Assert.Null(nothingWhileFinding);
        Assert.Null(picker.FindingText);
        Assert.Equal("No game's name has “qqzx”.", picker.NothingFound);
    }

    [Fact]
    public async Task AnAnswerToWhatWasTypedBeforeIsDropped()
    {
        GamePickerViewModel picker = Picker();
        TaskCompletionSource first = new();
        _lookup.HeldFinds.Enqueue(first);

        picker.Query = "persona";
        Task earlier = picker.Finding;
        picker.Query = "zelda";
        await picker.Finding;
        first.SetResult();
        await earlier;

        Assert.Equal(["The Legend of Zelda"], picker.Entries.Select(entry => entry.Name));
        Assert.Null(picker.Known(Royal.Game));
    }

    [Fact]
    public async Task AGameFoundByNameCanBeChosenAndStaysKnown()
    {
        GamePickerViewModel picker = Picker();
        picker.Query = "zelda";
        await picker.Finding;

        picker.ChooseCommand.Execute(null);

        Assert.Equal([Zelda.Game], _chosen);
        Assert.Equal(Zelda, picker.Known(Zelda.Game));
        Assert.Equal("The Legend of Zelda", picker.Picked?.Name);
    }

    [Fact]
    public async Task ASiteThatCantSearchWhatsTypedFindsNothing()
    {
        GamePickerViewModel picker = Picker();
        picker.ShowGames([Stardew]);
        _lookup.Refusals["FindGamesAsync"] = new ArgumentException("A word of 3 letters or more.");

        picker.Query = "st";
        await picker.Finding;

        Assert.Equal(["Stardew Valley"], picker.Entries.Select(entry => entry.Name));
        Assert.Empty(_logger.Entries);
    }

    private GamePickerViewModel Picker()
        => new(_pictures.Pictures, _sources, _chosen.Add, byName: new GamesByName(_lookup, _lookup.SearchableSources, _logger));
}
