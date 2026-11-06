using System.Net;

using ModShelf.Application.Settings;
using ModShelf.Application.State;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.QuickSearch;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.QuickSearch;

/// <summary>
/// Quick search: the recent searches and the screens with nothing typed; then the games, the library's mods and the
/// screens whose names have what's typed, and the settings that say it, each opening where it should, from the keyboard
/// too; and the searches that opened something remembered.
/// </summary>
public sealed class QuickSearchViewModelTests
{
    private static readonly LibraryMod SkyUi = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI");
    private static readonly LibraryMod Address = TestData.LibraryMod(TestData.Mod("32444"), "Address Library for SKSE Plugins");
    private static readonly LibraryMod Smapi = TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI");

    private readonly QuickSearchHarness _harness = new();

    private QuickSearchViewModel Search => _harness.Search;

    [Fact]
    public async Task WithNothingTypedShowsTheRecentSearchesThenTheScreens()
    {
        _harness.UiState.State = UiState.None with { QuickSearches = ["dragon", "old versions"] };

        await _harness.OpenAsync(SkyUi);

        Assert.True(Search.IsOpen);
        Assert.Equal(
            ["# Recent searches", "> dragon", "old versions", "# Screens", "Library", "Updates", "Browse", "Downloads", "Accounts", "Settings"],
            _harness.Lines());
        Assert.Null(Search.NothingFound);
    }

    [Fact]
    public async Task FindsTheGamesTheModsTheSettingsAndTheScreensByName()
    {
        await _harness.OpenAsync(SkyUi, Address, Smapi);

        Assert.Equal(
            [
                "# Games",
                "> Skyrim Special Edition — In your library · 2 mods",
                "# Mods in your library",
                "SkyUI — Skyrim Special Edition",
                "# Settings",
                "Games › Skyrim Special Edition",
            ],
            _harness.Type("sky"));
        Assert.Equal(["# Screens", "> Downloads", "# Settings", "Library › Where files go"], _harness.Type("down"));
        Assert.Equal(["# Settings", "> General › Theme"], _harness.Type("dark"));
    }

    [Fact]
    public async Task TheLibrarysGamesComeFirstTheOneWithMostModsFirstThenTheSitesOthers()
    {
        await _harness.OpenAsync(Smapi, SkyUi, Address);

        IReadOnlyList<string> found = _harness.Type("s");

        Assert.Equal(
            ["# Games", "> Skyrim Special Edition — In your library · 2 mods", "Stardew Valley — In your library · 1 mod", "# Mods in your library"],
            found.Take(4));
        Assert.Equal(["> Fallout 4 — On Nexus Mods"], _harness.Type("fallout").Skip(1));
    }

    [Fact]
    public async Task TheModsSayWhatsFoundForThem()
    {
        LibraryMod pinned = TestData.LibraryMod(TestData.Mod("3863"), "SKSE64").WithPinned(true);
        LibraryMod updated = TestData.LibraryMod(TestData.Mod("12604"), "SkyUI", watched: true, TestData.LibraryFile(SkyUi.Mod, "1", "SkyUI-5.1.7z", "5.1"))
            .WithUpdates([new LibraryUpdate(new ModFileRef(SkyUi.Mod, "2"), "SkyUI 5.2", "SkyUI-5.2.7z", "5.2", ModFileCategory.Main, TestData.Now, new ModFileRef(SkyUi.Mod, "1"), TestData.Now, TestData.ModPage)]);

        await _harness.OpenAsync(pinned, updated);

        Assert.Equal(
            ["# Mods in your library", "SKSE64 — Skyrim Special Edition · pinned", "SkyUI — Skyrim Special Edition · update found"],
            _harness.Type("sk").SkipWhile(line => line != "# Mods in your library").Take(3));
    }

    [Fact]
    public async Task ListsAtMostSixGamesEightModsAndSixSettings()
    {
        _harness.Lookup.Games[TestData.Nexus] =
            [.. Enumerable.Range(1, 9).Select(n => new GameInfo(new GameRef(TestData.Nexus, $"game{n}"), $"Game {n}"))];
        _harness.Settings.Listed.AddRange(
            Enumerable.Range(1, 9).Select(n => new SettingsSearchEntry(SettingsCategory.General, "General", $"Game part {n}", $"Game part {n}")));

        await _harness.OpenAsync([.. Enumerable.Range(1, 12).Select(n => TestData.LibraryMod(TestData.Mod($"{n}"), $"Game mod {n}"))]);
        _harness.Type("game");

        Assert.Equal(
            (QuickSearchViewModel.MostGames, QuickSearchViewModel.MostMods, QuickSearchViewModel.MostSettings),
            (Count(QuickSearchKind.Game), Count(QuickSearchKind.Mod), Count(QuickSearchKind.Setting)));
        Assert.Equal((6, 8, 6), (QuickSearchViewModel.MostGames, QuickSearchViewModel.MostMods, QuickSearchViewModel.MostSettings));
    }

    [Fact]
    public async Task HiddenGamesAndTheirModsAreLeftOut()
    {
        _harness.Hidden.Show(new AppSettings { Games = new Dictionary<GameRef, GameSettings> { [QuickSearchHarness.Stardew] = new() { Hidden = true } } });

        await _harness.OpenAsync(SkyUi, Smapi);

        Assert.DoesNotContain(_harness.Type("s"), line => line.Contains("Stardew", StringComparison.Ordinal) || line.Contains("SMAPI", StringComparison.Ordinal));
        Assert.Empty(_harness.Type("stardew"));
    }

    [Fact]
    public async Task NothingFoundSaysSo()
    {
        await _harness.OpenAsync(SkyUi);

        Assert.Empty(_harness.Type("zzz"));
        Assert.Equal("Nothing found for “zzz”.", Search.NothingFound);
        Assert.Null(Search.Highlighted);

        Search.ChooseCommand.Execute(null);
        Search.MoveDownCommand.Execute(null);
        Assert.True(Search.IsOpen);

        _harness.Type(string.Empty);
        Assert.Null(Search.NothingFound);
    }

    [Fact]
    public async Task TheArrowKeysMoveFromRowToRowAndEnterOpensTheOneHighlighted()
    {
        await _harness.OpenAsync(SkyUi, Smapi);
        _harness.Type("s");

        Search.MoveUpCommand.Execute(null);
        Assert.Equal("Skyrim Special Edition", Search.Highlighted!.Name);
        Search.MoveDownCommand.Execute(null);
        Search.MoveDownCommand.Execute(null);
        Search.MoveUpCommand.Execute(null);
        Assert.Equal("Stardew Valley", Search.Highlighted!.Name);
        Assert.Single(Search.Entries, entry => entry.IsHighlighted);

        for (int moves = 0; moves < Search.Entries.Count + 2; moves++)
        {
            Search.MoveDownCommand.Execute(null);
        }

        Assert.Same(Search.Entries[^1], Search.Highlighted);
        Assert.Equal("Games › Stardew Valley", Search.Highlighted!.Name);

        Search.ChooseCommand.Execute(null);

        Assert.Equal([QuickSearchHarness.Stardew], _harness.GameSettings.Opened);
        Assert.False(Search.IsOpen);
    }

    [Fact]
    public async Task TheRowHighlightedStaysWhileItsStillFound()
    {
        await _harness.OpenAsync(SkyUi, Smapi);
        _harness.Type("s");
        Search.MoveDownCommand.Execute(null);

        _harness.Type("s ");

        Assert.Equal("Stardew Valley", Search.Highlighted!.Name);
        Assert.Single(Search.Entries, entry => entry.IsHighlighted);
    }

    [Fact]
    public async Task AGameInTheLibraryOpensThere()
    {
        List<GameRef> opened = [];
        _harness.Screens.LibraryGameOpening += (_, game) => opened.Add(game);
        _harness.Navigation.NavigateTo(Screen.Downloads);
        await _harness.OpenAsync(SkyUi, Smapi);
        _harness.Type("stardew");

        Assert.Equal("Open in the Library", Search.Highlighted!.Action);
        Search.ChooseCommand.Execute(null);

        Assert.Equal([QuickSearchHarness.Stardew], opened);
        Assert.Equal(Screen.Library, _harness.Navigation.Current);
        Assert.False(Search.IsOpen);
    }

    [Fact]
    public async Task AnotherGameOpensInBrowse()
    {
        List<GameRef> opened = [];
        _harness.Screens.BrowseGameOpening += (_, game) => opened.Add(game);
        await _harness.OpenAsync(SkyUi);
        _harness.Type("fallout");

        Assert.Equal("Open in Browse", Search.Highlighted!.Action);
        Search.Highlighted!.OpenCommand.Execute(null);

        Assert.Equal([QuickSearchHarness.Fallout], opened);
        Assert.Equal(Screen.Browse, _harness.Navigation.Current);
    }

    [Fact]
    public async Task AModOpensItsPage()
    {
        List<ModRef> opened = [];
        _harness.Screens.ModOpening += (_, mod) => opened.Add(mod);
        _harness.Navigation.NavigateTo(Screen.Downloads);
        await _harness.OpenAsync(SkyUi, Smapi);
        _harness.Type("smapi");

        Search.ChooseCommand.Execute(null);

        Assert.Equal([Smapi.Mod], opened);
        Assert.Equal(Screen.Library, _harness.Navigation.Current);
    }

    [Fact]
    public async Task ASettingOpensItsCategoryAndAGameItsOwnSettings()
    {
        List<SettingsCategory> opened = [];
        _harness.Screens.SettingsOpening += (_, category) => opened.Add(category);
        await _harness.OpenAsync(SkyUi);

        _harness.Type("folder");
        Search.ChooseCommand.Execute(null);
        Assert.Equal([SettingsCategory.Library], opened);
        Assert.Equal(Screen.Settings, _harness.Navigation.Current);

        Search.OpenCommand.Execute(null);
        _harness.Type("skyrim");
        Search.Entries.Single(entry => entry.Kind == QuickSearchKind.Setting).OpenCommand.Execute(null);
        Assert.Equal([QuickSearchHarness.Skyrim], _harness.GameSettings.Opened);
        Assert.False(Search.IsOpen);
    }

    [Fact]
    public async Task ASearchThatOpenedSomethingIsRememberedTheNewestFirst()
    {
        await _harness.OpenAsync(SkyUi);
        _harness.Type("  fallout ");
        Search.ChooseCommand.Execute(null);
        await Search.Remembering;

        Search.OpenCommand.Execute(null);
        await Search.Reading;
        Assert.Equal(["# Recent searches", "> fallout"], _harness.Lines().Take(2));
        _harness.Type("updates");
        Search.ChooseCommand.Execute(null);
        await Search.Remembering;

        Assert.Equal(["updates", "fallout"], _harness.UiState.State.QuickSearches);
        Search.OpenCommand.Execute(null);
        await Search.Reading;
        Assert.Equal(["# Recent searches", "> updates", "fallout"], _harness.Lines().Take(3));
    }

    [Fact]
    public async Task ASearchIsAmongTheRecentOnesBeforeItsSaved()
    {
        await _harness.OpenAsync(SkyUi);
        TaskCompletionSource held = _harness.UiState.HoldNext();
        _harness.Type("fallout");
        Search.ChooseCommand.Execute(null);

        Search.OpenCommand.Execute(null);
        await Search.Reading;

        Assert.Equal(["# Recent searches", "> fallout"], _harness.Lines().Take(2));
        held.SetResult();
        await Search.Remembering;
        Assert.Equal(["fallout"], _harness.UiState.State.QuickSearches);
    }

    [Fact]
    public async Task ARecentSearchSearchesAgain()
    {
        _harness.UiState.State = UiState.None with { QuickSearches = ["sky"] };
        await _harness.OpenAsync(SkyUi);

        Search.ChooseCommand.Execute(null);

        Assert.Equal("sky", Search.Query);
        Assert.True(Search.IsOpen);
        Assert.Equal("> Skyrim Special Edition — In your library · 1 mod", _harness.Lines()[1]);
        Assert.Equal(["sky"], _harness.UiState.State.QuickSearches);
    }

    [Fact]
    public async Task ClosingForgetsWhatWasTypedAndOpeningReadsTheLibraryAgain()
    {
        await _harness.OpenAsync(SkyUi);
        _harness.Type("smapi");
        Assert.True(Search.CloseCommand.CanExecute(null));

        Search.CloseCommand.Execute(null);

        Assert.False(Search.IsOpen);
        Assert.Equal(string.Empty, Search.Query);
        Assert.False(Search.CloseCommand.CanExecute(null));

        await _harness.OpenAsync(Smapi);
        Assert.Contains("> SMAPI — Stardew Valley", _harness.Type("smapi"));
    }

    [Fact]
    public async Task WithoutTheSitesGamesTheLibrarysGoByTheirKeys()
    {
        _harness.Lookup.Refusals["GetGamesAsync"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await _harness.OpenAsync(SkyUi);

        Assert.Equal(["# Games", "> skyrimspecialedition — In your library · 1 mod"], _harness.Type("sky").Take(2));
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task ALibraryThatCantBeReadIsLoggedAndTheRestIsStillFound()
    {
        _harness.Library.ListRefusal = new InvalidOperationException("The database is locked.");

        await _harness.OpenAsync(SkyUi);

        Assert.StartsWith(
            "Error: Quick search couldn't read the library or the sites' games System.InvalidOperationException: The database is locked.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.Equal(["# Games", "> Fallout 4 — On Nexus Mods"], _harness.Type("fallout"));
    }

    [Fact]
    public async Task ASearchThatCantBeRememberedIsLogged()
    {
        _harness.UiState.Refusal = new IOException("The disk is full.");
        await _harness.OpenAsync();
        _harness.Type("updates");

        Search.ChooseCommand.Execute(null);
        await Search.Remembering;

        Assert.Equal(Screen.Updates, _harness.Navigation.Current);
        Assert.StartsWith(
            "Warning: Couldn't remember quick search's search System.IO.IOException: The disk is full.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGamesRowShowsItsCoverWhileTheLibraryShowsPictures()
    {
        await _harness.OpenAsync(SkyUi);
        _harness.Type("sky");
        QuickSearchEntryViewModel skyrim = Search.Entries[0];
        await skyrim.Picture!.Showing;

        Assert.Equal(ScriptedPictureCache.FileOf(QuickSearchHarness.SkyrimCover), skyrim.Picture.File);
        Assert.Equal(("S", true), (skyrim.Initial, skyrim.HasColour));
        Assert.Null(Search.Entries.Single(entry => entry.Kind == QuickSearchKind.Mod).Picture);
    }

    [Fact]
    public async Task AModsRowHasItsInitialOnItsGamesColour()
    {
        await _harness.OpenAsync(Address);
        _harness.Type("address");

        QuickSearchEntryViewModel mod = Assert.Single(Search.Entries);

        Assert.Equal(("A", Covers.HueOf("skyrimspecialedition"), true), (mod.Initial, mod.Hue, mod.HasColour));
        Assert.Null(mod.Picture);
    }

    [Fact]
    public async Task WithTheLibrarysPreviewsOffAGameShowsItsInitial()
    {
        _harness.Pictures.Settings.Stored = new AppSettings
        {
            Previews = new Dictionary<PreviewScreen, PreviewLevel> { [PreviewScreen.Library] = PreviewLevel.Off },
        };

        await _harness.OpenAsync(SkyUi);
        _harness.Type("sky");

        Assert.Null(Search.Entries[0].Picture!.File);
        Assert.Empty(_harness.Pictures.Cache.Asked);
    }

    [Fact]
    public void TheFieldSaysWhichKeysOpenIt()
    {
        string keys = OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl+K";

        Assert.Equal((keys, $"Quick search ({keys})"), (Search.Keys, Search.Tip));
    }

    private int Count(QuickSearchKind kind) => Search.Entries.Count(entry => entry.Kind == kind);
}
