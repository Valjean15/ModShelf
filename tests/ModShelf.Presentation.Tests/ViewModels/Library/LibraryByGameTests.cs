using ModShelf.Application.Catalog;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library by game: the games with what their mods add up to, in each order; a game's mods with what to know of
/// them, the toggles that narrow them, and each order; and the way between the games, a game and a mod.
/// </summary>
public sealed class LibraryByGameTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly LibraryHarness _harness = new();

    public LibraryByGameTests()
    {
        _harness.Catalog.GameNames["stardewvalley"] = "Stardew Valley";
    }

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task EachGameSaysWhatItsModsAddUpTo()
    {
        using CultureScope culture = new("en-US");
        LibraryMod skyUi = Mod(SkyUi, "SkyUI", checkedHoursAgo: 2, updates: 1, File(SkyUi, "1", 3 * 1024 * 1024), Archived(SkyUi, "0", 1024 * 1024));
        LibraryMod address = Mod(Address, "Address Library", checkedHoursAgo: 5, updates: 2, File(Address, "1", 1024 * 1024));

        await _harness.StartAsync(skyUi, address, TestData.LibraryMod(Smapi, "SMAPI"));
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        LibraryGameViewModel stardew = _harness.Game(Smapi.Game);

        Assert.Equal(
            ("2 mods", "3 updates", "5.00 MB", "1.00 MB archived", "2 hours ago"),
            (skyrim.ModsText, skyrim.UpdatesText, skyrim.SizeText, skyrim.ArchivedText, skyrim.CheckedText));
        Assert.Equal(
            "2 mods · 3 updates · 5.00 MB on disk, 1.00 MB of it archived versions · checked 2 hours ago",
            skyrim.SummaryText);
        Assert.Equal(("1 mod", "Up to date", false, "Not yet"), (stardew.ModsText, stardew.UpdatesText, stardew.HasArchived, stardew.CheckedText));
        Assert.Equal("1 mod · Up to date · 0 B on disk", stardew.SummaryText);
        Assert.Equal("2 games · 3 mods · 5.00 MB on disk · checked 2 hours ago", Screen.SummaryText);
        Assert.Equal(("S", "Open Skyrim Special Edition"), (skyrim.Initial, skyrim.OpenText));
    }

    [Fact]
    public async Task EachGameHasACoverHueOfItsOwnEveryTime()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        using LibraryHarness again = new();
        await again.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        int skyrim = _harness.Game(SkyUi.Game).CoverHue;

        // Pinned: a hue from string.GetHashCode would differ from one run of the app to the next.
        Assert.Equal(313, skyrim);
        Assert.Equal(172, _harness.Game(Smapi.Game).CoverHue);
        Assert.Equal(skyrim, again.Game(SkyUi.Game).CoverHue);
    }

    [Fact]
    public async Task AGameGoingByItsKeyHasItsCapitalInitial()
    {
        await _harness.StartAsync(TestData.LibraryMod(TestData.Mod("1", "zgame"), "Other"));

        Assert.Equal(("zgame", "Z"), (Screen.Games.Single().Name, Screen.Games.Single().Initial));
    }

    [Fact]
    public async Task AGameWithOneUpdateHasUpdates()
    {
        await _harness.StartAsync(Mod(SkyUi, "SkyUI", updates: 1), Mod(Smapi, "SMAPI", updates: 0));

        Assert.Equal((true, "1 update"), (_harness.Game(SkyUi.Game).HasUpdates, _harness.Game(SkyUi.Game).UpdatesText));
        Assert.False(_harness.Game(Smapi.Game).HasUpdates);
    }

    [Theory]
    [InlineData(GameOrder.MostMods, new[] { "Skyrim Special Edition", "Stardew Valley", "zgame" })]
    [InlineData(GameOrder.Name, new[] { "Skyrim Special Edition", "Stardew Valley", "zgame" })]
    [InlineData(GameOrder.MostSpace, new[] { "zgame", "Stardew Valley", "Skyrim Special Edition" })]
    [InlineData(GameOrder.UpdatesFirst, new[] { "Stardew Valley", "Skyrim Special Edition", "zgame" })]
    public async Task OrdersTheGames(GameOrder order, string[] expected)
    {
        ModRef other = TestData.Mod("1", "zgame");
        await _harness.StartAsync(
            Mod(SkyUi, "SkyUI", updates: 0, File(SkyUi, "1", 10)),
            Mod(Address, "Address Library", updates: 0, File(Address, "1", 10)),
            Mod(Smapi, "SMAPI", updates: 1, File(Smapi, "1", 100)),
            Mod(other, "Other", updates: 0, File(other, "1", 1000)));

        Screen.SelectedGameOrder = Screen.GameOrders.Single(choice => choice.Value == order);

        Assert.Equal(expected, Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task OpensAGameOnItsModsAndGoesBackToTheGames()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"), TestData.LibraryMod(Smapi, "SMAPI"));
        bool couldShowGames = Screen.ShowGamesCommand.CanExecute(null);

        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        bool isOpen = Screen.IsGameOpen;
        string[] shown = [.. Screen.GameMods.Select(mod => mod.Name)];
        Screen.ShowGamesCommand.Execute(null);

        Assert.False(couldShowGames);
        Assert.True(isOpen);
        Assert.Equal(["Address Library", "SkyUI"], shown);
        Assert.Equal((false, null), (Screen.IsGameOpen, Screen.OpenGame));
        Assert.Empty(Screen.GameMods);
    }

    [Fact]
    public async Task AModOpensOverItsGameAndBackGoesToItsGame()
    {
        _harness.Lookup.Pages[SkyUi] = Page(SkyUi, "SkyUI");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        LibraryModViewModel row = _harness.Row(TestData.LibraryMod(SkyUi, "SkyUI"));

        await row.OpenCommand.ExecuteAsync(null);
        (bool, bool) open = (Screen.IsModOpen, Screen.ShowGamesCommand.CanExecute(null));
        Screen.BackCommand.Execute(null);

        Assert.Equal((true, true), open);
        Assert.False(Screen.IsModOpen);
        Assert.Equal(SkyUi.Game, Screen.OpenGame?.Game);
    }

    [Fact]
    public async Task BackFromAnotherGamesModGoesToThatGame()
    {
        _harness.Lookup.Pages[SkyUi] = Page(SkyUi, "SkyUI") with { Requirements = [new ModRequirement("SMAPI", Smapi)] };
        _harness.Lookup.Pages[Smapi] = Page(Smapi, "SMAPI");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        await _harness.Row(TestData.LibraryMod(SkyUi, "SkyUI")).OpenCommand.ExecuteAsync(null);

        await Screen.Mod!.Requirements[0].OpenCommand.ExecuteAsync(null);
        string crumb = Screen.BackText;
        Screen.BackCommand.Execute(null);

        Assert.Equal("Stardew Valley", crumb);
        Assert.Equal(Smapi.Game, Screen.OpenGame?.Game);
        Assert.Equal(["SMAPI"], Screen.GameMods.Select(mod => mod.Name));
    }

    [Fact]
    public async Task OpeningAGameClosesTheModAndShowingTheGamesClosesBoth()
    {
        _harness.Lookup.Pages[SkyUi] = Page(SkyUi, "SkyUI");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        await _harness.Row(TestData.LibraryMod(SkyUi, "SkyUI")).OpenCommand.ExecuteAsync(null);

        Screen.ShowGamesCommand.Execute(null);

        Assert.Equal((false, false), (Screen.IsModOpen, Screen.IsGameOpen));
    }

    [Fact]
    public async Task AModWhoseGameLeftGoesBackToTheGames()
    {
        _harness.Lookup.Pages[Smapi] = Page(Smapi, "SMAPI");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        await _harness.Row(TestData.LibraryMod(Smapi, "SMAPI")).OpenCommand.ExecuteAsync(null);

        // Removed from its page, the game's only mod.
        _harness.Library.Change(Smapi, null);
        (bool, bool, bool) left = (Screen.IsModOpen, Screen.IsGameOpen, Screen.ShowGamesCommand.CanExecute(null));
        Screen.ShowGamesCommand.Execute(null);

        Assert.Equal((true, false, true), left);
        Assert.False(Screen.IsModOpen);
        Assert.Equal(["Skyrim Special Edition"], Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task AnOpenGameWhoseLastModLeftShowsTheGames()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Smapi, "SMAPI"));
        _harness.Game(Smapi.Game).OpenCommand.Execute(null);

        _harness.Library.Change(Smapi, null);

        Assert.False(Screen.IsGameOpen);
        Assert.Equal(["Skyrim Special Edition"], Screen.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task EachModSaysYourVersionItsFilesAndTheirSize()
    {
        using CultureScope culture = new("en-US");
        LibraryFile optional = new(new ModFileRef(SkyUi, "9"), "Fonts", "9.9", ModFileCategory.Optional, TestData.Now, "nexus/skyrimspecialedition/Mod (12604)/Fonts.7z", 1024, TestData.Now);
        LibraryMod skyUi = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z", "5.2", size: 2 * 1024 * 1024, daysAgo: 3), optional, Archived(SkyUi, "1", 1024 * 1024));

        await _harness.StartAsync(skyUi, TestData.LibraryMod(Address, "Address Library"));
        LibraryModViewModel row = _harness.Row(skyUi);
        LibraryModViewModel watchedOnly = _harness.Row(TestData.LibraryMod(Address, "Address Library"));

        // The main file's version, though the optional file is newer.
        Assert.Equal(("5.2", "2 files, 1 archived", "3.00 MB"), (row.VersionText, row.FilesText, row.SizeText));
        Assert.Equal(("—", "No files", "—"), (watchedOnly.VersionText, watchedOnly.FilesText, watchedOnly.SizeText));
    }

    [Fact]
    public async Task EachModSaysWhatToKnowOfIt()
    {
        LibraryFile yours = TestData.LibraryFile(SkyUi, "1", "SkyUI.7z", "5.1");
        LibraryMod update = WithUpdates(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, yours), Update(SkyUi, "2", "5.2", ModFileCategory.Main, yours.File));
        LibraryMod optional = WithUpdates(TestData.LibraryMod(Address, "Address Library", watched: true, File(Address, "1", 10)), Update(Address, "2", "1.0", ModFileCategory.Optional, null));
        ModRef many = TestData.Mod("1");
        LibraryMod updates = WithUpdates(TestData.LibraryMod(many, "Many", watched: true, File(many, "1", 10)), Update(many, "2", "2.0", ModFileCategory.Main, null), Update(many, "3", "3.0", ModFileCategory.Main, null));
        ModRef watched = TestData.Mod("2");
        ModRef unwatched = TestData.Mod("3");
        ModRef main = TestData.Mod("4");
        LibraryFile kept = File(unwatched, "1", 10);
        LibraryFile mains = File(main, "1", 10);
        _harness.Storage.Files.Add(Path(yours));
        _harness.Storage.Files.Add(Path(kept));
        _harness.Storage.Files.Add(Path(mains));

        await _harness.StartAsync(
            update,
            optional,
            updates,
            TestData.LibraryMod(watched, "Watched"),
            TestData.LibraryMod(unwatched, "Unwatched", watched: false, kept),
            WithUpdates(TestData.LibraryMod(main, "Main", watched: true, mains), Update(main, "2", "2.0", ModFileCategory.Main, null)));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;

        Assert.Equal(
            [
                "Address Library: New optional file (Update), 1 file missing (Problem)",
                "Main: New file (Update)",
                "Many: 2 updates (Update), 1 file missing (Problem)",
                "SkyUI: Update to 5.2 (Update)",
                "Unwatched: Not watched (Note)",
                "Watched: Watched only (Note)",
            ],
            Screen.GameMods.Select(mod => $"{mod.Name}: {string.Join(", ", mod.Statuses.Select(status => $"{status.Text} ({status.Kind})"))}"));
    }

    [Fact]
    public async Task SaysWhenEachModsSiteLastUpdatedIt()
    {
        _harness.Pictures.Details.Saved[SkyUi] = new ModInfo(SkyUi, "SkyUI", updatedAt: TestData.Now.AddDays(-2));

        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), TestData.LibraryMod(Address, "Address Library"));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;

        Assert.Equal(["Address Library: —", "SkyUI: 2 days ago"], Screen.GameMods.Select(mod => $"{mod.Name}: {mod.UpdatedText}"));

        // The game's saved details at once, never a read a mod (ADR 0084).
        Assert.Equal([2], _harness.Pictures.Details.AskedAtOnce);
    }

    [Fact]
    public async Task AGamesFilesAreLookedForInItsOwnFolder()
    {
        string skyrim = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [SkyUi.Game] = new() { Root = skyrim } },
        };
        LibraryFile there = File(SkyUi, "1", 10);
        LibraryFile gone = File(SkyUi, "2", 10);
        _harness.Storage.Files.Add(LibraryPaths.Resolve(skyrim, there.RelativePath));

        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, there, gone));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;

        // Only the file that's in neither folder is missing; the one in the game's folder isn't.
        Assert.Equal(1, Assert.Single(Screen.GameMods).MissingCount);
    }

    [Fact]
    public async Task FilesMissingArentCheckedWhenTheLibrarysFolderIsntKnown()
    {
        _harness.Settings.Refusal = new InvalidDataException("settings.json isn't JSON.");

        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, File(SkyUi, "1", 10)));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;

        Assert.Equal(0, Assert.Single(Screen.GameMods).MissingCount);
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("Checking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGameOpenedLaterDropsTheCheckOfTheOneBefore()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, File(SkyUi, "1", 10)), TestData.LibraryMod(Smapi, "SMAPI"));
        _harness.UiThread.Inline = false;
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        LibraryModViewModel skyUi = Screen.GameMods.Single();
        await Screen.Checking;

        _harness.Game(Smapi.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        _harness.UiThread.RunPending();

        Assert.Equal(0, skyUi.MissingCount);
        Assert.Empty(skyUi.Statuses);
    }

    [Fact]
    public async Task AModThatChangesInTheOpenGameIsCheckedAgain()
    {
        LibraryFile first = File(SkyUi, "1", 10);
        _harness.Storage.Files.Add(Path(first));
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, first));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        int before = Screen.GameMods.Single().MissingCount;

        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: true, first, File(SkyUi, "2", 10)));
        await Screen.Checking;

        Assert.Equal((0, 1), (before, Screen.GameMods.Single().MissingCount));
    }

    [Fact]
    public async Task TheTogglesNarrowTheModsAndCountThem()
    {
        LibraryFile yours = TestData.LibraryFile(SkyUi, "1", "SkyUI.7z");
        _harness.Storage.Files.Add(Path(yours));
        LibraryMod updated = WithUpdates(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, yours), Update(SkyUi, "2", "2.0", ModFileCategory.Main, yours.File));
        LibraryMod archived = TestData.LibraryMod(Address, "Address Library", watched: false, Archived(Address, "1", 10));
        ModRef missing = TestData.Mod("1");

        await _harness.StartAsync(updated, archived, TestData.LibraryMod(missing, "Missing", watched: true, File(missing, "1", 10)));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        await Screen.Checking;
        int[] counts = [.. Screen.ModFilters.Select(toggle => toggle.Count)];
        List<string[]> shown = [];
        foreach (ModFilterViewModel toggle in Screen.ModFilters)
        {
            toggle.IsOn = true;
            shown.Add([.. Screen.GameMods.Select(mod => mod.Name)]);
            toggle.IsOn = false;
        }

        Screen.ModFilters[1].IsOn = true;
        Screen.ModFilters[3].IsOn = true;

        Assert.Equal([1, 2, 1, 2], counts);
        Assert.Equal([["SkyUI"], ["Missing", "SkyUI"], ["Address Library"], ["Address Library", "Missing"]], shown);
        Assert.Equal(["Missing"], Screen.GameMods.Select(mod => mod.Name));
        Assert.Equal(counts, Screen.ModFilters.Select(toggle => toggle.Count));
    }

    [Fact]
    public async Task TheFilterFindsModsByNameOrAuthor()
    {
        LibraryMod byOther = LibraryMod.Restore(Address, "Address Library", "meh321", null, isWatched: true, TestData.Now, null, []);

        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"), byOther);
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);
        Screen.ModsFilter = " SKY ";
        string[] byName = [.. Screen.GameMods.Select(mod => mod.Name)];
        Screen.ModsFilter = "MEH";
        string[] byAuthor = [.. Screen.GameMods.Select(mod => mod.Name)];
        Screen.ModsFilter = "zzz";

        Assert.Equal(["SkyUI"], byName);
        Assert.Equal(["Address Library"], byAuthor);
        Assert.True(Screen.NoModMatches);
    }

    [Theory]
    [InlineData(ModOrder.Name, new[] { "A", "B", "C" })]
    [InlineData(ModOrder.UpdatesFirst, new[] { "C", "A", "B" })]
    [InlineData(ModOrder.UpdatedOnSite, new[] { "B", "A", "C" })]
    [InlineData(ModOrder.Size, new[] { "A", "C", "B" })]
    [InlineData(ModOrder.AddedToLibrary, new[] { "C", "B", "A" })]
    public async Task OrdersAGamesMods(ModOrder order, string[] expected)
    {
        ModRef a = TestData.Mod("1");
        ModRef b = TestData.Mod("2");
        ModRef c = TestData.Mod("3");
        LibraryFile cFile = File(c, "1", 50);
        _harness.Pictures.Details.Saved[a] = new ModInfo(a, "A", updatedAt: TestData.Now.AddDays(-5));
        _harness.Pictures.Details.Saved[b] = new ModInfo(b, "B", updatedAt: TestData.Now.AddDays(-1));
        await _harness.StartAsync(
            LibraryMod.Restore(a, "A", null, null, isWatched: true, TestData.Now.AddDays(-9), null, [File(a, "1", 100)]),
            LibraryMod.Restore(b, "B", null, null, isWatched: true, TestData.Now.AddDays(-5), null, []),
            LibraryMod.Restore(c, "C", null, null, isWatched: true, TestData.Now.AddDays(-1), null, [cFile], [Update(c, "2", "2.0", ModFileCategory.Main, cFile.File)]));
        _harness.Game(a.Game).OpenCommand.Execute(null);
        await Screen.Checking;

        Screen.SelectedModOrder = Screen.ModOrders.Single(choice => choice.Value == order);

        Assert.Equal(expected, Screen.GameMods.Select(mod => mod.Name));
    }

    [Fact]
    public async Task UpdatesFirstKeepsTheModsWithUpdatesByName()
    {
        ModRef c = TestData.Mod("3");
        await _harness.StartAsync(Mod(SkyUi, "A", updates: 1), Mod(Address, "B", updates: 2), Mod(c, "C", updates: 0));
        _harness.Game(SkyUi.Game).OpenCommand.Execute(null);

        Screen.SelectedModOrder = Screen.ModOrders.Single(choice => choice.Value == ModOrder.UpdatesFirst);

        Assert.Equal(["A", "B", "C"], Screen.GameMods.Select(mod => mod.Name));
    }

    private static string Path(LibraryFile file) => LibraryPaths.Resolve(TestData.LibraryRoot, file.RelativePath);

    private static ModPage Page(ModRef mod, string name)
        => new(new GameInfo(mod.Game, mod.Game.Key), new ModInfo(mod, name, pageUrl: new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}")), [], [], null);

    private static LibraryFile File(ModRef mod, string id, long size) => TestData.LibraryFile(mod, id, $"{mod.Id}-{id}.7z", size: size);

    private static LibraryFile Archived(ModRef mod, string id, long size)
        => new(new ModFileRef(mod, id), $"Old {id}", "0.1", ModFileCategory.Main, TestData.Now.AddDays(-60), $"nexus/{mod.Game.Key}/Mod ({mod.Id})/_archive/old-{id}.7z", size, TestData.Now.AddDays(-50));

    private static LibraryUpdate Update(ModRef mod, string id, string version, ModFileCategory category, ModFileRef? replaces)
        => new(new ModFileRef(mod, id), $"File {id}", $"file-{id}.7z", version, category, TestData.Now, replaces, TestData.Now);

    private static LibraryMod WithUpdates(LibraryMod mod, params LibraryUpdate[] updates) => mod.WithUpdates(updates);

    private static LibraryMod Mod(ModRef mod, string name, int updates, params LibraryFile[] files)
        => LibraryMod.Restore(mod, name, "An Author", null, isWatched: true, TestData.Now.AddDays(-30), null, files, [.. Enumerable.Range(1, updates).Select(n => Update(mod, $"u{n}", $"{n}.0", ModFileCategory.Main, null))]);

    private static LibraryMod Mod(ModRef mod, string name, int checkedHoursAgo, int updates, params LibraryFile[] files)
        => LibraryMod.Restore(mod, name, "An Author", null, isWatched: true, TestData.Now.AddDays(-30), TestData.Now.AddHours(-checkedHoursAgo), files, [.. Enumerable.Range(1, updates).Select(n => Update(mod, $"u{n}", $"{n}.0", ModFileCategory.Main, null))]);
}
