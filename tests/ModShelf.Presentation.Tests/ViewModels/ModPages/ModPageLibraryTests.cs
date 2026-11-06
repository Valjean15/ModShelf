using System.Collections.Immutable;
using System.Globalization;

using ModShelf.Application.Catalog;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.ModPages;

/// <summary>
/// What a mod's page says of the library's files of it, of its site's files to the library, of its changes, of what it
/// needs, and what it does to the mod in the library.
/// </summary>
public sealed class ModPageLibraryTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("32444");

    private readonly BrowseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheLibrarysFilesSayingWhichAreMissing()
    {
        using CultureScope culture = new("en-US");
        LibraryFile newer = TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z", daysAgo: 1);
        LibraryFile older = TestData.LibraryFile(SkyUi, "1", "SkyUI_5_1.7z", daysAgo: 9);
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [older, newer]));
        _harness.Storage.Files.Add(LibraryPaths.Resolve(TestData.LibraryRoot, newer.RelativePath));

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal(["SkyUI_5_2.7z", "SkyUI_5_1.7z"], mod.OwnFiles.Select(file => file.FileName));
        Assert.Equal([false, true], mod.OwnFiles.Select(file => file.IsMissing));
        Assert.Equal("Main", mod.OwnFiles[0].CategoryText);
        Assert.Equal("Downloaded 10/26/2026", mod.OwnFiles[0].DownloadedText);
        Assert.Equal("nexus/skyrimspecialedition/Mod (12604)", mod.OwnFolderText);
        Assert.True(mod.HasOwnFiles);
        Assert.False(mod.HasArchived);
    }

    [Fact]
    public async Task LooksForAGamesFilesInItsOwnFolder()
    {
        string skyrim = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [SkyUi.Game] = new() { Root = skyrim } },
        };
        LibraryFile there = TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z", daysAgo: 1);
        LibraryFile inTheRoot = TestData.LibraryFile(SkyUi, "1", "SkyUI_5_1.7z", daysAgo: 9);
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [inTheRoot, there]));
        _harness.Storage.Files.Add(LibraryPaths.Resolve(skyrim, there.RelativePath));
        _harness.Storage.Files.Add(LibraryPaths.Resolve(TestData.LibraryRoot, inTheRoot.RelativePath));

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal([false, true], mod.OwnFiles.Select(file => file.IsMissing));
    }

    [Fact]
    public async Task SaysNothingOfMissingFilesWhenTheLibrarysFolderIsntKnown()
    {
        _harness.Settings.Refusal = new InvalidDataException("settings.json isn't JSON.");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")]));

        ModPageViewModel mod = await OpenAsync();

        Assert.False(Assert.Single(mod.OwnFiles).IsMissing);
    }

    [Fact]
    public async Task ShowsEachFilesSha256ShortenedAndCopiesItWhole()
    {
        const string Sha = "3f9a61c0d2b74e5aa19c0f6e8d3b2a1c5e7f90b4c6d8e2a1f3b5c7d98be2c21e";
        LibraryFile hashed = Hashed(TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z", daysAgo: 1), Sha);
        LibraryFile unhashed = TestData.LibraryFile(SkyUi, "1", "SkyUI_5_1.7z", daysAgo: 9);
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [hashed, unhashed]));
        ModPageViewModel mod = await OpenAsync();

        await mod.OwnFiles[0].CopySha256Command.ExecuteAsync(null);

        Assert.Equal(["3f9a61c0…8be2c21e", string.Empty], mod.OwnFiles.Select(file => file.Sha256Text));
        Assert.Equal([Sha], _harness.Clipboard.Texts);
        Assert.False(mod.OwnFiles[1].CopySha256Command.CanExecute(null));
    }

    [Fact]
    public async Task SaysSoWhenTheSha256CantBeCopied()
    {
        LibraryFile hashed = Hashed(TestData.LibraryFile(SkyUi, "1", "SkyUI.7z"), new string('a', 64));
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [hashed]));
        ModPageViewModel mod = await OpenAsync();
        _harness.Clipboard.Refusal = new InvalidOperationException("No clipboard.");

        await mod.OwnFiles[0].CopySha256Command.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Messages);
        Assert.Contains(_harness.Logger.Entries, entry => entry.Contains("CopySha256", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShowsArchivedVersionsWhenAsked()
    {
        LibraryFile current = TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z");
        LibraryFile archived = new(
            new ModFileRef(SkyUi, "1"),
            "SkyUI 5.1",
            "5.1",
            ModFileCategory.Main,
            TestData.Now.AddDays(-20),
            "nexus/skyrimspecialedition/Mod (12604)/_archive/SkyUI_5_1.7z",
            1024,
            TestData.Now.AddDays(-19));
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [current, archived]));

        ModPageViewModel mod = await OpenAsync();
        (int, string) hidden = (mod.OwnFiles.Count, mod.ArchivedText);
        mod.ToggleArchivedCommand.Execute(null);

        Assert.Equal((1, "Show 1 archived version"), hidden);
        Assert.Equal(["SkyUI_5_2.7z", "_archive/SkyUI_5_1.7z"], mod.OwnFiles.Select(file => file.FileName));
        Assert.True(mod.OwnFiles[1].IsArchived);
        Assert.Equal("Hide the archived versions", mod.ArchivedText);
        Assert.Equal("nexus/skyrimspecialedition/Mod (12604)", mod.OwnFolderText);
    }

    [Fact]
    public async Task SaysTheFolderOfTheModsCurrentFilesEvenWhenAnArchivedOneIsNewer()
    {
        LibraryFile current = TestData.LibraryFile(SkyUi, "2", "SkyUI_5_2.7z", daysAgo: 9);
        LibraryFile archived = new(
            new ModFileRef(SkyUi, "1"),
            "SkyUI 5.1",
            "5.1",
            ModFileCategory.Main,
            TestData.Now.AddDays(-20),
            "nexus/skyrimspecialedition/Mod (12604)/_archive/SkyUI_5_1.7z",
            1024,
            TestData.Now.AddDays(-1));
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [current, archived]));

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal("nexus/skyrimspecialedition/Mod (12604)", mod.OwnFolderText);
    }

    [Fact]
    public async Task SaysWhenTheModWasLastChecked()
    {
        using CultureScope culture = new("en-US");
        LibraryMod checkedMod = LibraryMod.Restore(SkyUi, "SkyUI", null, null, isWatched: true, TestData.Now.AddDays(-30), TestData.Now.AddHours(-2), []);
        await _harness.StartAsync(checkedMod);

        ModPageViewModel mod = await OpenAsync();

        // The space before AM is the one the system's culture data puts there: a narrow one with ICU's.
        string space = new DateTime(2026, 1, 1, 13, 0, 0).ToString("t", CultureInfo.GetCultureInfo("en-US"))[4..^2];
        Assert.Equal($"10/27/2026 10:00{space}AM", mod.Facts.Single(fact => fact.Label == "Last checked").Value);
    }

    [Fact]
    public async Task SaysWhatEachFileOnTheSiteIsToTheLibrary()
    {
        LibraryFile yours = TestData.LibraryFile(SkyUi, "1", "SkyUI_5_1.7z", version: "5.1");
        LibraryMod inLibrary = LibraryMod.Restore(
            SkyUi,
            "SkyUI",
            null,
            null,
            isWatched: true,
            TestData.Now.AddDays(-30),
            TestData.Now,
            [yours],
            [
                new LibraryUpdate(new ModFileRef(SkyUi, "2"), "SkyUI 5.2", "SkyUI_5_2.7z", "5.2", ModFileCategory.Main, TestData.Now, yours.File, TestData.Now, null),
                new LibraryUpdate(new ModFileRef(SkyUi, "3"), "Fonts", "Fonts.7z", "1.0", ModFileCategory.Optional, TestData.Now, null, TestData.Now, null),
            ]);
        await _harness.StartAsync(inLibrary);

        ModPageViewModel mod = await OpenAsync(
            BrowseHarness.File(SkyUi, "1"),
            BrowseHarness.File(SkyUi, "2"),
            BrowseHarness.File(SkyUi, "3", ModFileCategory.Optional),
            BrowseHarness.File(SkyUi, "4", ModFileCategory.Optional));

        Assert.Equal(
            ["1: in library", "2: Update for your 5.1", "3: New optional file", "4: "],
            mod.Files.OrderBy(file => file.File.Id, StringComparer.Ordinal).Select(file => $"{file.File.Id}: {(file.IsInLibrary ? "in library" : file.UpdateNote)}"));
        Assert.True(mod.Files.Single(file => file.File.Id == "2").IsUpdate);
    }

    [Fact]
    public async Task AnUpdateNotOfferedIsntSaidToBeOne()
    {
        LibraryFile yours = TestData.LibraryFile(SkyUi, "1", "SkyUI_5_1.7z", version: "5.1");
        LibraryMod inLibrary = LibraryMod.Restore(
            SkyUi,
            "SkyUI",
            null,
            null,
            isWatched: true,
            TestData.Now.AddDays(-30),
            TestData.Now,
            [yours],
            [
                new LibraryUpdate(new ModFileRef(SkyUi, "2"), "SkyUI 5.2", "SkyUI_5_2.7z", "5.2", ModFileCategory.Main, TestData.Now, yours.File, TestData.Now, null),
                new LibraryUpdate(new ModFileRef(SkyUi, "3"), "Fonts", "Fonts.7z", "1.0", ModFileCategory.Optional, TestData.Now, null, TestData.Now, null),
            ],
            skippedUpdates: [new ModFileRef(SkyUi, "3")]);
        await _harness.StartAsync(inLibrary);

        ModPageViewModel mod = await OpenAsync(BrowseHarness.File(SkyUi, "1"), BrowseHarness.File(SkyUi, "2"), BrowseHarness.File(SkyUi, "3", ModFileCategory.Optional));

        Assert.Equal(["2: Update for your 5.1", "3: "], mod.Files.Where(file => file.File.Id != "1").Select(file => $"{file.File.Id}: {file.UpdateNote}"));
        Assert.Equal((false, false, string.Empty, (string?)null), (mod.HasKept, mod.IsPinned, mod.PinnedText, mod.UserNote));

        await _harness.Library.SetPinnedAsync(SkyUi, true, TestContext.Current.CancellationToken);
        await _harness.Library.SetNoteAsync(SkyUi, "Stay on 5.1.", TestContext.Current.CancellationToken);

        Assert.Equal(["2: ", "3: "], mod.Files.Where(file => file.File.Id != "1").Select(file => $"{file.File.Id}: {file.UpdateNote}"));
        Assert.Equal((true, true, "Pinned at 5.1: its updates aren't offered.", "Stay on 5.1."), (mod.HasKept, mod.IsPinned, mod.PinnedText, mod.UserNote));
    }

    [Fact]
    public async Task APinnedModWithoutAVersionSaysPinnedAndANoteAloneShows()
    {
        await _harness.StartAsync(LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now).WithPinned(true));
        ModPageViewModel mod = await OpenAsync(BrowseHarness.File(SkyUi, "1"));

        Assert.Equal("Pinned: its updates aren't offered.", mod.PinnedText);

        await _harness.Library.SetPinnedAsync(SkyUi, false, TestContext.Current.CancellationToken);
        await _harness.Library.SetNoteAsync(SkyUi, "Waiting for 2.0.", TestContext.Current.CancellationToken);

        Assert.Equal((true, false, string.Empty, "Waiting for 2.0."), (mod.HasKept, mod.IsPinned, mod.PinnedText, mod.UserNote));
    }

    [Fact]
    public async Task SaysTheModsDetailsAndWhenItCameIntoTheLibrary()
    {
        using CultureScope culture = new("en-US");
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        ModPageViewModel mod = await OpenAsync(info: new ModInfo(SkyUi, "SkyUI", pageUrl: TestData.ModPage, category: "User Interface"));

        Assert.Equal("User Interface", mod.CategoryText);
        Assert.Equal(
            ["Mod ID: 12604", "Category: User Interface", "In the library since: 9/27/2026", "Last checked: Not yet"],
            mod.Facts.Select(fact => $"{fact.Label}: {fact.Value}"));
    }

    [Fact]
    public async Task AModOutsideTheLibraryHasOnlyItsSitesDetails()
    {
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync();

        Assert.Equal(["Mod ID: 12604"], mod.Facts.Select(fact => $"{fact.Label}: {fact.Value}"));
        Assert.Equal((false, true, true), (mod.IsOffline, mod.ShowsNoFiles, mod.ShowsNoChangelog));
        Assert.False(mod.HasCategory);
        Assert.False(mod.HasOwnFiles);
        Assert.False(mod.IsInLibrary);
        Assert.False(mod.RescanCommand.CanExecute(null));
        Assert.False(mod.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowsTheLatestChangesNewestFirstThenAll()
    {
        ModChangelog[] changelog = [.. Enumerable.Range(1, 5).Select(minor => new ModChangelog($"1.{minor}", [$"Change {minor}."]))];
        await _harness.StartAsync();

        // The site lists the oldest first, ending with the mod's version, 1.5.
        ModPageViewModel mod = await OpenAsync(info: new ModInfo(SkyUi, "SkyUI", version: "1.5", pageUrl: TestData.ModPage), changelog: changelog);
        IReadOnlyList<string> latest = [.. mod.ShownChanges.Select(entry => entry.Version)];
        string more = mod.MoreChangesText;
        mod.ToggleChangesCommand.Execute(null);

        Assert.Equal(["1.5", "1.4", "1.3"], latest);
        Assert.Equal("All 5 entries", more);
        Assert.Equal(["1.5", "1.4", "1.3", "1.2", "1.1"], mod.ShownChanges.Select(entry => entry.Version));
        Assert.Equal("Only the latest", mod.MoreChangesText);
        Assert.True(mod.HasMoreChanges);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("2.0")]
    [InlineData(null)]
    public async Task KeepsTheSitesOrderUnlessItEndsWithTheModsVersion(string? version)
    {
        ModChangelog[] changelog = [new("1.1", ["A."]), new("1.0", ["B."])];
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync(info: new ModInfo(SkyUi, "SkyUI", version: version, pageUrl: TestData.ModPage), changelog: changelog);

        Assert.Equal(["1.1", "1.0"], mod.ShownChanges.Select(entry => entry.Version));
        Assert.False(mod.HasMoreChanges);
    }

    [Fact]
    public async Task KeepsTheSitesOrderWhenItStartsWithTheModsVersionToo()
    {
        ModChangelog[] changelog = [new("1.0", ["Again."]), new("0.9", ["B."]), new("1.0", ["A."])];
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync(info: new ModInfo(SkyUi, "SkyUI", version: "1.0", pageUrl: TestData.ModPage), changelog: changelog);

        Assert.Equal(["Again.", "B.", "A."], mod.ShownChanges.Select(entry => entry.Changes[0]));
    }

    [Fact]
    public async Task ListsWhatTheModNeedsAndWhichTheLibraryHas()
    {
        await _harness.StartAsync(TestData.LibraryMod(Address, "Address Library", files: [TestData.LibraryFile(Address, "1", "Address.7z")]));
        ModRequirement[] needs =
        [
            new("Address Library", Address, notes: "For every file."),
            new("SKSE", pageUrl: new Uri("https://skse.silverlock.org/")),
            new("A DLC"),
        ];

        ModPageViewModel mod = await OpenAsync(requirements: needs);

        Assert.True(mod.HasRequirements);
        Assert.Equal(
            ["Address Library (For every file.): in library, mod", "SKSE (): elsewhere", "A DLC (): "],
            mod.Requirements.Select(need => $"{need.Name} ({need.Notes}): {string.Join(", ", new[] { need.IsInLibrary ? "in library" : null, need.IsMod ? "mod" : null, need.IsElsewhere ? "elsewhere" : null }.OfType<string>())}"));
        Assert.False(mod.Requirements[2].OpenCommand.CanExecute(null));
        Assert.False(mod.Requirements[2].OpenPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARequirementTheLibraryGetsShowsItHasIt()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await OpenAsync(requirements: [new("Address Library", Address)]);
        bool before = mod.Requirements[0].IsInLibrary;

        _harness.Library.Change(Address, TestData.LibraryMod(Address, "Address Library", files: [TestData.LibraryFile(Address, "1", "Address.7z")]));

        Assert.False(before);
        Assert.True(mod.Requirements[0].IsInLibrary);
    }

    [Fact]
    public async Task OpensAModItNeedsOnTheScreen()
    {
        await _harness.StartAsync();
        _harness.Lookup.Pages[Address] = BrowseHarness.Page(BrowseHarness.Info(Address, "Address Library"));
        ModPageViewModel mod = await OpenAsync(requirements: [new("Address Library", Address)]);

        await mod.Requirements[0].OpenCommand.ExecuteAsync(null);
        await _harness.Screen.Listing;

        Assert.Equal(Address, _harness.Screen.Mod?.Mod);
    }

    [Fact]
    public async Task OpensTheSiteOfAThingOffIt()
    {
        await _harness.StartAsync();
        Uri skse = new("https://skse.silverlock.org/");
        ModPageViewModel mod = await OpenAsync(requirements: [new("SKSE", pageUrl: skse)]);

        await mod.Requirements[0].OpenPageCommand.ExecuteAsync(null);

        Assert.Equal([skse], _harness.Browser.Opened);
    }

    [Fact]
    public async Task RescansTheModsFoldersAndSaysWhatItFound()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        ModPageViewModel mod = await OpenAsync();

        await mod.RescanCommand.ExecuteAsync(null);

        Assert.Equal([TestData.ModPage], _harness.Rescan.Asked);
        Assert.Equal("Rescan finished", Assert.Single(_harness.Dialogs.Messages).Title);
    }

    [Fact]
    public async Task RemovesTheModFromTheLibraryAtOnceOfferingToUndoIt()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        ModPageViewModel mod = await OpenAsync(BrowseHarness.Info(SkyUi, "SkyUI from the site"));

        await mod.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["Forget 12604"], _harness.Library.Asked);
        Assert.False(mod.IsInLibrary);
        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Equal("SkyUI removed from the library; its files stay in its folder.", _harness.Undo.Message);

        await _harness.Undo.UndoCommand.ExecuteAsync(null);
        Assert.True(mod.IsInLibrary);
    }

    [Fact]
    public async Task UnwatchingOffersToUndoIt()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")]));
        ModPageViewModel mod = await OpenAsync();

        await mod.ToggleWatchCommand.ExecuteAsync(null);

        Assert.Equal((false, "SkyUI is no longer watched."), (mod.IsWatched, _harness.Undo.Message));
        await _harness.Undo.UndoCommand.ExecuteAsync(null);
        Assert.True(mod.IsWatched);
    }

    [Fact]
    public async Task OpensTheModsFolder()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")]));
        ModPageViewModel mod = await OpenAsync();

        await mod.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal([Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "Mod (12604)")], _harness.Folders.Opened);
    }

    [Fact]
    public async Task OpensTheModsFolderInItsGamesOwnFolder()
    {
        string skyrim = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [SkyUi.Game] = new() { Root = skyrim } },
        };
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", files: [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")]));
        ModPageViewModel mod = await OpenAsync();

        await mod.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal([Path.Combine(skyrim, "nexus", "skyrimspecialedition", "Mod (12604)")], _harness.Folders.Opened);
    }

    [Theory]
    [InlineData(PreviewLevel.Off, false)]
    [InlineData(PreviewLevel.Thumbnails, true)]
    [InlineData(PreviewLevel.Cards, true)]
    public async Task ShowsTheModsPictureAsTheScreensPreviewsSay(PreviewLevel level, bool shown)
    {
        _harness.Pictures.Settings.Stored = new AppSettings
        {
            Previews = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty.Add(PreviewScreen.Browse, level),
        };
        Uri picture = new("https://staticdelivery.nexusmods.com/mods/1704/images/12604/skyui.png");
        await _harness.StartAsync();

        ModPageViewModel mod = await OpenAsync(info: new ModInfo(SkyUi, "SkyUI", pageUrl: TestData.ModPage, pictureUrl: picture));
        await mod.Picture.Showing;

        Assert.Equal(shown ? ScriptedPictureCache.FileOf(picture) : null, mod.Picture.File);
        Assert.Same(_harness.Screen.Previews, mod.Previews);
    }

    private static LibraryFile Hashed(LibraryFile file, string sha256)
        => new(file.File, file.Name, file.VersionLabel, file.Category, file.UploadedAt, file.RelativePath, file.SizeInBytes, file.DownloadedAt, sha256);

    private Task<ModPageViewModel> OpenAsync(params ModFile[] files) => OpenAsync(null, files);

    private Task<ModPageViewModel> OpenAsync(
        ModInfo? info = null,
        IReadOnlyList<ModFile>? files = null,
        IReadOnlyList<ModChangelog>? changelog = null,
        IReadOnlyList<ModRequirement>? requirements = null)
    {
        ModInfo mod = info ?? BrowseHarness.Info(SkyUi, "SkyUI");
        ModPage page = BrowseHarness.Page(mod, [.. files ?? []]) with
        {
            Changelogs = changelog ?? [],
            Requirements = requirements ?? [],
        };
        return _harness.OpenAsync(page);
    }
}
