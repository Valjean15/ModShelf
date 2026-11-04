using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// Rescanning against a site whose SkyUI has 5.2 (35407) and 5.3 (35408), and whose SkyUI Survival has one file, each
/// 100 bytes as the site lists them. The library's folders follow the default pattern unless a test says otherwise.
/// </summary>
public sealed class LibraryRescannerTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string Survival = "3863";

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _history = new();
    private readonly FakeModSite _site = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingLogger<LibraryRescanner> _logger = new();
    private readonly SettingsService _settings;

    public LibraryRescannerTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, "35407", "SkyUI_5_2_SE.7z", "SkyUI 5.2", "5.2");
        _site.AddFile(SkyUi, "35408", "SkyUI_5_3_SE.7z", "SkyUI 5.3", "5.3", category: ModFileCategory.Optional);
        _site.AddMod(Survival, "SkyUI Survival");
        _site.AddFile(Survival, "1", "Survival.7z", "Survival");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    private string SkyUiFolder => InRoot("fake-mods/skyrimspecialedition/SkyUI (12604)");

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task AddsAFilePlacedByHandInAModsFolderAndNeverMovesOrDeletesAnything()
    {
        // As Female Nude Blend's: a file of a mod the library has, downloaded outside ModShelf into its folder.
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        string placed = Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z");
        _storage.Add(placed);
        using WatchedChanges seen = new(_changes);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        LibraryFile added = Assert.Single(rescan.Added);
        Assert.Equal(
            (new ModFileRef(Mod(SkyUi), "35408"), "SkyUI 5.3", "5.3", ModFileCategory.Optional, "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"),
            (added.File, added.Name, added.VersionLabel, added.Category, added.RelativePath));
        Assert.Equal((100L, TestData.Now, _storage.HashOf(placed)), (added.SizeInBytes, added.DownloadedAt, added.Sha256));
        LibraryMod mod = Assert.Single(_library.Mods);
        Assert.Equal(["35407", "35408"], mod.Files.Select(file => file.File.Id));
        Assert.True(mod.NeedsFullCheck);
        Assert.Equal((1, 0, 0, 0, 0), (rescan.Mods, rescan.Hashed.Count, rescan.Missing.Count, rescan.Unmatched.Count, rescan.Failures.Count));
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
        Assert.Empty(_storage.Moves);
        Assert.Equal(2, _storage.Files.Count);
        Assert.Single(await seen.StopAsync());
        Assert.Equal(
            [
                "Information: Added SkyUI_5_3_SE.7z, found on disk, to the library as a file of mod 12604 of skyrimspecialedition ",
                "Information: Rescanned 1 mods: 1 files added, 0 hashed, 0 missing, 0 not matched, 0 mods not matched ",
            ],
            _logger.Entries);
    }

    [Fact]
    public async Task ReadsTheHashesTheLibraryLacksAndSaysWhichFilesAreMissingWithoutAskingTheSite()
    {
        Have(
            SkyUi,
            Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"),
            Stored("35408", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"));
        _storage.Remove(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        string path = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z");
        Assert.Equal(("35407", _storage.HashOf(path)), (Assert.Single(rescan.Hashed).File.Id, rescan.Hashed[0].Sha256));
        Assert.Equal("35408", Assert.Single(rescan.Missing).File.Id);
        Assert.Equal([_storage.HashOf(path), null], Assert.Single(_library.Mods).Files.Select(file => file.Sha256));
        Assert.False(Assert.Single(_library.Mods).NeedsFullCheck);
        Assert.Empty(rescan.Added);
        Assert.Empty(_site.Asked);
        Assert.Equal([path], _storage.Hashed);
    }

    [Fact]
    public async Task AFileWithAHashIsntReadAgainAndOneThatCantBeReadIsLeftWithout()
    {
        Have(
            SkyUi,
            Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)),
            Stored("35408", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"));
        string locked = Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z");
        _storage.Failures[locked] = new IOException("Another program has it locked.");

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Hashed);
        Assert.Equal([locked], _storage.Hashed);
        Assert.Equal([new string('a', 64), null], Assert.Single(_library.Mods).Files.Select(file => file.Sha256));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith(
            "Warning: Couldn't read SkyUI_5_3_SE.7z for its SHA-256 while rescanning System.IO.IOException: Another program has it locked.",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileADownloadMovesWhileItsReadIsLeftToTheDownload()
    {
        // As an old version archived as a newer one is recorded: the library has it elsewhere by the time it's read.
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        LibraryFile archived = Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/_archive/SkyUI_5_2_SE.7z");
        _storage.BeforeHash = _ => _library.UpdateAsync(Mod(SkyUi), mod => mod!.WithFile(archived), Cancellation).GetAwaiter().GetResult();

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Hashed);
        LibraryFile kept = Assert.Single(Assert.Single(_library.Mods).Files);
        Assert.Equal((archived.RelativePath, (string?)null), (kept.RelativePath, kept.Sha256));
    }

    [Theory]
    [InlineData("SkyUI_5_3_SE.7z", 100)]
    [InlineData("skyui_5_3_se.7z", 100)]
    [InlineData("SkyUI_5_3_SE (35408).7z", 100)]
    [InlineData("SkyUI_5_3_SE.7z", 1124)]
    [InlineData("SkyUI_5_3_SE.7z", 0)]
    public async Task MatchesByNameAsTheLibrarySavesItAndBySizeWithinAKilobyte(string name, long size)
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, name), size);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(("35408", size), (Assert.Single(rescan.Added).File.Id, rescan.Added[0].SizeInBytes));
    }

    [Theory]
    [InlineData("SkyUI_5_3_SE.7z", 1125)]
    [InlineData("SkyUI_5_3_SE (35407).7z", 100)]
    [InlineData("SkyUI_5_3.7z", 100)]
    [InlineData("Readme.txt", 100)]
    public async Task AFileOfAnotherNameOrSizeIsntMatched(string name, long size)
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        string other = Path.Combine(SkyUiFolder, name);
        _storage.Add(other, size);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Added);
        Assert.Equal([other], rescan.Unmatched);
        Assert.Single(Assert.Single(_library.Mods).Files);
    }

    [Fact]
    public async Task AFileThatMatchesTwoOfTheSitesFilesIsntGuessed()
    {
        // As an author who uploads each version under the same name.
        _site.AddFile(SkyUi, "35409", "SkyUI_5_3_SE.7z", "SkyUI 5.3 again", "5.3");
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        string placed = Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z");
        _storage.Add(placed);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Added);
        Assert.Equal([placed], rescan.Unmatched);
    }

    [Fact]
    public async Task AFileTheLibraryHasElsewhereIsntAddedAgain()
    {
        Have(SkyUi, Stored("35407", "kept/SkyUI_5_2_SE.7z", new string('a', 64)));
        string copy = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z");
        _storage.Add(copy);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Added);
        Assert.Equal([copy], rescan.Unmatched);
    }

    [Fact]
    public async Task LooksInTheModsArchiveFolderToo()
    {
        Have(SkyUi, Stored("35408", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z", new string('a', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, "_archive", "SkyUI_5_2_SE.7z"));

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal("fake-mods/skyrimspecialedition/SkyUI (12604)/_archive/SkyUI_5_2_SE.7z", Assert.Single(rescan.Added).RelativePath);
    }

    [Fact]
    public async Task BringsAModNotInTheLibraryInWatchedByItsLink()
    {
        // As a mod forgotten, or one whose files were placed by hand before it was ever downloaded.
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"));

        LibraryRescan rescan = await Rescanner().RescanAsync(Link(SkyUi, "35407"), Cancellation);

        LibraryMod mod = Assert.Single(_library.Mods);
        Assert.Equal(("SkyUI", "Someone", true, TestData.Now), (mod.Name, mod.Author, mod.IsWatched, mod.AddedAt));
        Assert.Equal("35407", Assert.Single(mod.Files).File.Id);
        Assert.Equal((1, "35407"), (rescan.Mods, Assert.Single(rescan.Added).File.Id));
        Assert.Equal(["RecognizeAsync", "GetModAsync", "GetFilesAsync"], _site.Asked);
    }

    [Fact]
    public async Task AModNotInTheLibraryWithNothingOnDiskStaysOut()
    {
        LibraryRescan rescan = await Rescanner().RescanAsync(Link(SkyUi, "35407"), Cancellation);

        Assert.Empty(_library.Mods);
        Assert.Empty(rescan.Added);
        Assert.Equal(["RecognizeAsync", "GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task ALinkRescansOnlyItsMod()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        Have(Survival, Stored("1", "fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Survival.7z"));

        // Its page in the library: the site isn't asked who it is.
        LibraryRescan rescan = await Rescanner().RescanAsync(new Uri($"https://mods.example/{Survival}"), Cancellation);

        Assert.Equal((1, "1"), (rescan.Mods, Assert.Single(rescan.Hashed).File.Id));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task ModsSharingAFolderMatchEachTheirOwn()
    {
        _store.Settings = new AppSettings { LibraryFolders = FolderPattern.Parse("{source}/{game}/{file}") };
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI_5_2_SE.7z", new string('a', 64)));
        Have(Survival, Stored("9", "fake-mods/skyrimspecialedition/Old.7z", new string('b', 64)));
        string shared = InRoot("fake-mods/skyrimspecialedition");
        _storage.Add(Path.Combine(shared, "SkyUI_5_3_SE.7z"));
        _storage.Add(Path.Combine(shared, "Survival.7z"));
        _storage.Add(Path.Combine(shared, "Readme.txt"));

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(["35408", "1"], rescan.Added.Select(file => file.File.Id));
        Assert.Equal([Path.Combine(shared, "Readme.txt")], rescan.Unmatched);
    }

    [Fact]
    public async Task ASiteThatRefusesIsAFailureAndIsntAskedAgain()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        Have(Survival, Stored("9", "fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Old.7z"));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));
        _storage.Add(InRoot("fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Survival.7z"));
        _site.Failures[nameof(FakeModSite.GetFilesAsync)] = new HttpRequestException("Slow down.", null, HttpStatusCode.TooManyRequests);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(
            [
                new RescanFailure(Mod(SkyUi), "Slow down."),
                new RescanFailure(Mod(Survival), "Not asked: fake-mods refused an earlier question in this rescan. Slow down."),
            ],
            rescan.Failures);
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
        Assert.Empty(rescan.Added);
        Assert.Empty(rescan.Unmatched);
        Assert.Equal(2, rescan.Hashed.Count);
    }

    [Fact]
    public async Task AnotherProblemWithTheSiteStopsOnlyItsMod()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        Have(Survival, Stored("9", "fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Old.7z", new string('b', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));
        _storage.Add(InRoot("fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Survival.7z"));
        _site.ModFailures[SkyUi] = new HttpRequestException("No such mod.", null, HttpStatusCode.NotFound);

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(new RescanFailure(Mod(SkyUi), "No such mod."), Assert.Single(rescan.Failures));
        Assert.Equal("1", Assert.Single(rescan.Added).File.Id);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't ask the site about mod 12604 of skyrimspecialedition to rescan it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFolderThatCantBeListedIsLeftOut()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));
        _storage.ListFailures[SkyUiFolder] = new UnauthorizedAccessException("Access denied.");

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Empty(rescan.Added);
        Assert.Empty(_site.Asked);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith($"Warning: Couldn't list {SkyUiFolder} to rescan it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEmptyLibraryRescansNothing()
    {
        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(
            (0, 0, 0, 0, 0, 0),
            (rescan.Mods, rescan.Added.Count, rescan.Hashed.Count, rescan.Missing.Count, rescan.Unmatched.Count, rescan.Failures.Count));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task AFileFoundOnDiskIsKeptInTheHistory()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));
        _time.Advance(TimeSpan.FromMinutes(5));

        await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal(
            new LibraryEvent(
                TestData.Now.AddMinutes(5),
                LibraryEventKind.Found,
                Mod(SkyUi),
                "SkyUI",
                new ModFileRef(Mod(SkyUi), "35408"),
                "SkyUI 5.3",
                "5.3",
                "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"),
            Assert.Single(_history.Events));
    }

    [Fact]
    public async Task AModBroughtInByItsLinkIsKeptInTheHistoryByItsFilesFound()
    {
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));

        await Rescanner().RescanAsync(Link(SkyUi, "35407"), Cancellation);

        Assert.Equal(
            [(LibraryEventKind.Found, "35407", "SkyUI"), (LibraryEventKind.Found, "35408", "SkyUI")],
            _history.Events.Select(happened => (happened.Kind, happened.File!.Id, happened.ModName)).Order());
    }

    [Fact]
    public async Task HashingAndMissingFilesArentInTheHistory()
    {
        Have(
            SkyUi,
            Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"),
            Stored("35408", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"));
        _storage.Remove(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal((1, 1), (rescan.Hashed.Count, rescan.Missing.Count));
        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task AHistoryThatCantKeepWhatWasFoundLeavesItAdded()
    {
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));
        _history.Failure = new InvalidDataException("The database is locked.");

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal("35408", Assert.Single(rescan.Added).File.Id);
        Assert.Equal(2, Assert.Single(_library.Mods).Files.Count);
    }

    [Fact]
    public async Task LooksForAGamesFilesInItsOwnFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _store.Settings = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer)
            {
                [new GameRef(FakeModSite.SourceId, "skyrimspecialedition")] = new() { Root = folder },
            },
        };
        Have(SkyUi, Stored("35407", "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", new string('a', 64)));
        _storage.Remove(InRoot("fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        _storage.Add(LibraryPaths.Resolve(folder, "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        _storage.Add(LibraryPaths.Resolve(folder, "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"));
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_3_SE.7z"));

        LibraryRescan rescan = await Rescanner().RescanAsync(null, Cancellation);

        Assert.Equal("fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z", Assert.Single(rescan.Added).RelativePath);
        Assert.Empty(rescan.Missing);
        Assert.Equal(LibraryPaths.Resolve(folder, "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"), Assert.Single(_storage.Hashed));
    }

    private static LibraryFile Stored(string fileId, string relativePath, string? sha256 = null)
        => new(File(SkyUi, fileId), $"File {fileId}", null, ModFileCategory.Main, TestData.Now.AddDays(-9), relativePath, 100, TestData.Now.AddDays(-2), sha256);

    /// <summary>
    /// Puts the mod in the library, watched, with its files on disk.
    /// </summary>
    private void Have(string modId, params LibraryFile[] files)
    {
        LibraryMod mod = LibraryMod.Restore(
            Mod(modId), modId == SkyUi ? "SkyUI" : "SkyUI Survival", null, new Uri($"https://mods.example/{modId}"), true, TestData.Now.AddDays(-9), null,
            files.Select(file => new LibraryFile(new ModFileRef(Mod(modId), file.File.Id), file.Name, file.VersionLabel, file.Category, file.UploadedAt, file.RelativePath, file.SizeInBytes, file.DownloadedAt, file.Sha256)));
        _library.Add(mod);
        foreach (LibraryFile file in files)
        {
            _storage.Add(InRoot(file.RelativePath));
        }
    }

    private string InRoot(string relativePath) => LibraryPaths.Resolve(Root, relativePath);

    private LibraryRescanner Rescanner() => new(new SourceRegistry([_site]), _library, _settings, _storage, _changes, _history.History(_time), _time, _logger);
}
