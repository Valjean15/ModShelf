using System.Text;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Archives;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Archives;

/// <summary>
/// Importing another mod manager's downloads, against a site whose SkyUI has 5.2 (35407) and 5.3 (35408), whose SkyUI
/// Survival has one file, and whose mod 500 has one, each 100 bytes as the site lists them; it finds files by MD5. The
/// archives are in a folder outside the library, which follows the default pattern.
/// </summary>
public sealed class ArchiveImportTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string Survival = "3863";
    private const string Other = "500";
    private const string SkyUi52 = "SkyUI_5_2_SE-12604-5-2SE-1573160372.7z";
    private const string SkyUi53 = "SkyUI_5_3_SE-12604-5-3SE-1600000000.7z";

    private static readonly string Downloads = Path.Combine(Path.GetTempPath(), "MO2", "downloads");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _history = new();
    private readonly FakeModSite _site = new();
    private readonly FakeLookupSite _lookup;
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly FakeLibraryHold _hold = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingLogger<ArchiveImport> _logger = new();
    private readonly List<string> _deleted = [];
    private readonly SettingsService _settings;

    public ArchiveImportTests()
    {
        _lookup = new FakeLookupSite(_site);
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, "35407", SkyUi52, "SkyUI 5.2", "5.2");
        _site.AddFile(SkyUi, "35408", SkyUi53, "SkyUI 5.3", "5.3", category: ModFileCategory.Optional);
        _site.AddMod(Survival, "SkyUI Survival");
        _site.AddFile(Survival, "1", "Survival.7z", "Survival");
        _site.AddMod(Other, "Other");
        _site.AddFile(Other, "9", "other.zip", "Other");
        _storage.BeforeDelete = _deleted.Add;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task ScansTheFoldersArchivesWithWhatTheirNotesAndNamesSayAskingNoSite()
    {
        string noted = Archive("SkyUI_5_2_SE.7z");
        Note(noted, "[General]\r\ngameName=SkyrimSE\r\nmodID=12604\r\nfileID=35407\r\nrepository=Nexus\r\n");
        Archive(SkyUi53);
        Archive("survival.7z");
        _storage.Add(Path.Combine(Downloads, "readme.txt"));
        _storage.Add(Path.Combine(Downloads, "Nested", "inner.7z"));

        ArchiveScan scan = await Import().ScanAsync(Downloads + Path.DirectorySeparatorChar, Game, Cancellation);

        Assert.Equal(
            [
                new ScannedArchive(noted, 100, ArchiveClue.Notes, SkyUi, "35407"),
                new ScannedArchive(Path.Combine(Downloads, SkyUi53), 100, ArchiveClue.Name, SkyUi, null),
                new ScannedArchive(Path.Combine(Downloads, "survival.7z"), 100, null, null, null),
            ],
            scan.Archives);
        Assert.Equal((Downloads, Game, 2, true), (scan.Folder, scan.Game, scan.OtherFiles, scan.CanLookUp));

        // SkyUI's files, and its details as it's new to the library; then the archive no note or name names.
        Assert.Equal(3, scan.Requests);
        Assert.Empty(_site.Asked);
        Assert.Empty(_lookup.Searched);
    }

    [Fact]
    public async Task RefusesAFolderInTheLibraryAndASiteThatCantListItsModsFiles()
    {
        ArgumentException inLibrary = await Assert.ThrowsAsync<ArgumentException>(
            () => Import().ScanAsync(Path.Combine(Root, "fake-mods"), Game, Cancellation));
        ArgumentException noCatalog = await Assert.ThrowsAsync<ArgumentException>(
            () => Import().ScanAsync(Downloads, new GameRef(new SourceId("elsewhere"), "game"), Cancellation));

        Assert.Contains("is in the library", inLibrary.Message, StringComparison.Ordinal);
        Assert.StartsWith("elsewhere can't list its mods' files", noCatalog.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentifiesByNoteByNameAndByHashTheCheapestWayFirst()
    {
        string noted = Archive("SkyUI_5_2_SE.7z");
        Note(noted, "[General]\nmodID=12604\nfileID=35407\n");
        string named = Archive(SkyUi53);
        string mystery = Archive("mystery.7z");
        string lookedUp = Archive("other-a.zip");
        string twin = Archive("other-b.zip");
        _storage.Hashes[twin] = _storage.HashOf(lookedUp);
        string kept = Archive("survival.7z");
        Have(Survival, new LibraryFile(File(Survival, "1"), "Survival", "1.0", ModFileCategory.Main, TestData.Now.AddDays(-9), "fake-mods/skyrimspecialedition/SkyUI Survival (3863)/Survival.7z", 100, TestData.Now.AddDays(-2), _storage.HashOf(kept)));
        _lookup.ByMd5[_storage.Md5Of(lookedUp)] = [(Other, "9")];
        List<int> progress = [];

        ArchivePlan plan = await IdentifyAsync(new Progress(progress));

        Assert.Equal(
            [
                (noted, ArchiveState.Identified, (ArchiveClue?)ArchiveClue.Notes, "35407", "SkyUI", "SkyUI 5.2"),
                (named, ArchiveState.Identified, ArchiveClue.Name, "35408", "SkyUI", "SkyUI 5.3"),
                (mystery, ArchiveState.Unknown, null, null, null, null),
                (lookedUp, ArchiveState.Identified, ArchiveClue.Hash, "9", "Mod 500", "Other"),
                (twin, ArchiveState.Duplicate, null, null, null, null),
                (kept, ArchiveState.InLibrary, null, "1", "SkyUI Survival", "Survival"),
            ],
            plan.Archives.Select(archive => (archive.Path, archive.State, archive.By, archive.File?.Id, archive.ModName, archive.FileName)));
        Assert.Equal("Fake mod site has no file of the game with its content: it may be from elsewhere, or changed since.", plan.Archives[2].Why);
        Assert.Equal("It's the same as other-a.zip.", plan.Archives[4].Why);
        Assert.Equal([noted, named, lookedUp], plan.ToImport.Select(archive => archive.Path));
        Assert.Equal((2, 3, 1, 1, 1), (plan.NewMods, plan.Count(ArchiveState.Identified), plan.Count(ArchiveState.InLibrary), plan.Count(ArchiveState.Duplicate), plan.Count(ArchiveState.Unknown)));
        Assert.Equal(_storage.HashOf(noted), plan.Archives[0].Sha256);

        // One request for SkyUI's files and one for its details, and one search each for the archives nothing named.
        Assert.Equal(["GetFilesAsync 12604", "GetModAsync 12604"], _site.AskedFor);
        Assert.Equal([_storage.Md5Of(mystery), _storage.Md5Of(lookedUp)], _lookup.Searched);
        Assert.Equal([1, 2, 3, 4, 5, 6], progress);
        Assert.Equal(6, _storage.Hashed.Count);
        Assert.Contains("Information: Identified 6 archives in 4 requests: 3 to import, 1 in the library, 1 duplicates, 1 unknown ", _logger.Entries);
    }

    [Fact]
    public async Task ANoteOrANameThatMisleadsIsLookedUpInstead()
    {
        // Its note names a file the mod doesn't have, and its name a mod the site doesn't have.
        string noted = Archive("SkyUI_5_2_SE.7z");
        Note(noted, "[General]\nmodID=12604\nfileID=99999\n");
        string named = Archive("Survival-777-1-0-1600000000.7z");
        _lookup.ByMd5[_storage.Md5Of(noted)] = [(SkyUi, "35407")];
        _lookup.ByMd5[_storage.Md5Of(named)] = [(Survival, "1")];

        ArchivePlan plan = await IdentifyAsync();

        Assert.Equal(
            [(ArchiveState.Identified, (ArchiveClue?)ArchiveClue.Hash, "35407"), (ArchiveState.Identified, ArchiveClue.Hash, "1")],
            plan.Archives.Select(archive => (archive.State, archive.By, archive.File?.Id)));
        Assert.Equal(["GetFilesAsync 12604", "GetFilesAsync 777"], _site.AskedFor);
    }

    [Fact]
    public async Task OfSeveralFilesWithTheSameContentTheOneNamedSoOrOfTheLibrarysModIsIt()
    {
        _site.AddFile(Other, "10", "reupload.zip", "Reupload");
        string named = Archive("other.zip");
        string ofLibrary = Archive("copy.zip");
        string neither = Archive("third.zip");
        _lookup.ByMd5[_storage.Md5Of(named)] = [(Other, "10"), (Other, "9")];
        _lookup.ByMd5[_storage.Md5Of(ofLibrary)] = [(Survival, "1"), (Other, "10")];
        _lookup.ByMd5[_storage.Md5Of(neither)] = [(Other, "9"), (SkyUi, "35407")];
        Have(Survival);

        ArchivePlan plan = await IdentifyAsync();

        Assert.Equal(
            [(ofLibrary, ArchiveState.Identified, "1"), (named, ArchiveState.Identified, "9"), (neither, ArchiveState.Unknown, null)],
            plan.Archives.Select(archive => (archive.Path, archive.State, archive.File?.Id)));
        Assert.Equal("Fake mod site has 2 files of the game with its content, of several mods.", plan.Archives[2].Why);
    }

    [Fact]
    public async Task ARefusedAccountStopsTheLookupsTheArchivesLeftSayingWhy()
    {
        Archive("a.7z");
        Archive("b.7z");
        _lookup.Refusal = new AuthenticationFailedException("Link an account.");

        ArchivePlan plan = await IdentifyAsync();

        Assert.Equal(
            ["Fake mod site couldn't look it up: Link an account.", "Not looked up: Fake mod site refused an earlier request. Link an account."],
            plan.Archives.Select(archive => archive.Why));
        Assert.All(plan.Archives, archive => Assert.Equal(ArchiveState.Unknown, archive.State));
        Assert.Single(_lookup.Searched);
    }

    [Fact]
    public async Task WithoutALookupOnlyNotesAndNamesIdentify()
    {
        _lookup.LookupAlgorithm = FileHashAlgorithm.Sha512;
        Archive(SkyUi52);
        Archive("Survival-777-1-0-1600000000.7z");
        Archive("plain.7z");

        ArchiveScan scan = await Import().ScanAsync(Downloads, Game, Cancellation);
        ArchivePlan plan = await Import().IdentifyAsync(scan, null, Cancellation);

        Assert.Equal((false, 4), (scan.CanLookUp, scan.Requests));
        Assert.Equal([ArchiveState.Identified, ArchiveState.Unknown, ArchiveState.Unknown], plan.Archives.Select(archive => archive.State));
        Assert.Equal(
            [
                "Its name gives mod 777, but none of that mod's files on Fake mod site matches it.",
                "Fake mod site can't find archives by their content, and neither a note nor its name says which mod it's of.",
            ],
            plan.Archives.Skip(1).Select(archive => archive.Why));
        Assert.Empty(_lookup.Searched);
    }

    [Fact]
    public async Task CopiesTheIdentifiedArchivesWhereDownloadsGoWatchingTheirModsAndLeavingTheOriginals()
    {
        string noted = Archive("SkyUI_5_2_SE.7z");
        Note(noted, "[General]\nmodID=12604\nfileID=35407\n");
        string named = Archive(SkyUi53);
        string lookedUp = Archive("other-a.zip");
        Archive("mystery.7z");
        _lookup.ByMd5[_storage.Md5Of(lookedUp)] = [(Other, "9")];
        ArchivePlan plan = await IdentifyAsync();
        int asked = _site.AskedFor.Count;
        using WatchedChanges seen = new(_changes);
        List<int> progress = [];

        ArchiveImportResult result = await Import().ImportAsync(plan, new Progress(progress), Cancellation);

        string skyUiFolder = InRoot("fake-mods/skyrimspecialedition/SkyUI (12604)");
        Assert.Equal(
            [
                (noted, Path.Combine(skyUiFolder, SkyUi52)),
                (named, Path.Combine(skyUiFolder, SkyUi53)),
                (lookedUp, InRoot("fake-mods/skyrimspecialedition/Mod 500 (500)/other.zip")),
            ],
            _storage.Copies);
        Assert.Equal((3, 2, 0), (result.Imported.Count, result.ModsAdded, result.InLibrary));
        Assert.Empty(result.Problems);
        LibraryMod skyUi = Assert.Single(_library.Mods, mod => mod.Mod == Mod(SkyUi));
        Assert.Equal(("SkyUI", "Someone", true), (skyUi.Name, skyUi.Author, skyUi.IsWatched));
        LibraryFile first = skyUi.Files[0];
        Assert.Equal(
            (File(SkyUi, "35407"), "SkyUI 5.2", "5.2", ModFileCategory.Main, $"fake-mods/skyrimspecialedition/SkyUI (12604)/{SkyUi52}", 100L, TestData.Now, _storage.HashOf(noted)),
            (first.File, first.Name, first.VersionLabel, first.Category, first.RelativePath, first.SizeInBytes, first.DownloadedAt, first.Sha256));
        Assert.Equal(["35407", "35408"], skyUi.Files.Select(file => file.File.Id));
        Assert.Equal("Mod 500", Assert.Single(_library.Mods, mod => mod.Mod == Mod(Other)).Name);
        Assert.Equal(
            [LibraryEventKind.Found, LibraryEventKind.Found, LibraryEventKind.Found],
            _history.Events.Select(happened => happened.Kind));
        Assert.Equal(3, (await seen.StopAsync()).Count);

        // Only read: the originals stay, nothing is moved or deleted, and no site is asked.
        Assert.All([noted, named, lookedUp], path => Assert.True(_storage.FileExists(path)));
        Assert.Empty(_storage.Moves);
        Assert.Empty(_deleted);
        Assert.Equal(asked, _site.AskedFor.Count);
        Assert.Equal((3, false), (_hold.Holds, _hold.IsHeld));
        Assert.Equal([1, 2, 3], progress);
    }

    [Fact]
    public async Task AModTheLibraryHasKeepsItsFolderAndItsWatching()
    {
        LibraryFile mine = new(File(SkyUi, "35407"), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now.AddDays(-9), "Mine/SkyUI/SkyUI.7z", 100, TestData.Now.AddDays(-2), new string('a', 64));
        _library.Add(LibraryMod.Restore(Mod(SkyUi), "SkyUI", null, new Uri("https://mods.example/12604"), false, TestData.Now.AddDays(-9), null, [mine]));
        _storage.Add(InRoot(mine.RelativePath));
        string named = Archive(SkyUi53);
        ArchivePlan plan = await IdentifyAsync();

        ArchiveImportResult result = await Import().ImportAsync(plan, null, Cancellation);

        Assert.Equal((named, InRoot($"Mine/SkyUI/{SkyUi53}")), Assert.Single(_storage.Copies));
        Assert.Equal(0, result.ModsAdded);
        LibraryMod skyUi = Assert.Single(_library.Mods);
        Assert.False(skyUi.IsWatched);
        Assert.Equal(2, skyUi.Files.Count);
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
    }

    [Fact]
    public async Task StoppedHalfwayItGoesOnWhereItWasWhenRunAgain()
    {
        string first = Archive(SkyUi52);
        string second = Archive(SkyUi53);
        string third = Archive("other-a.zip");
        _lookup.ByMd5[_storage.Md5Of(third)] = [(Other, "9")];
        ArchivePlan plan = await IdentifyAsync();
        _storage.BeforeCopy = destination =>
        {
            if (destination.EndsWith(SkyUi53, StringComparison.Ordinal))
            {
                throw new OperationCanceledException();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Import().ImportAsync(plan, null, Cancellation));
        _storage.BeforeCopy = null;
        ArchivePlan again = await IdentifyAsync();
        ArchiveImportResult result = await Import().ImportAsync(again, null, Cancellation);

        Assert.Equal([ArchiveState.InLibrary, ArchiveState.Identified, ArchiveState.Identified], again.Archives.Select(archive => archive.State));
        Assert.Equal([first, second, third], _storage.Copies.Select(copy => copy.Source));
        Assert.Equal(2, result.Imported.Count);
        Assert.False(_hold.IsHeld);

        // A plan imported again finds them all in the library.
        ArchiveImportResult once = await Import().ImportAsync(plan, null, Cancellation);
        Assert.Equal((0, 3), (once.Imported.Count, once.InLibrary));
        Assert.Equal(3, _storage.Copies.Count);
    }

    [Fact]
    public async Task AnArchiveThatCantBeCopiedDoesntStopTheOthers()
    {
        string changed = Archive(SkyUi52);
        string roomy = Archive(SkyUi53, size: 100);
        string tooBig = Archive("other-a.zip", size: 5000);
        _lookup.ByMd5[_storage.Md5Of(tooBig)] = [(Other, "9")];
        ArchivePlan plan = await IdentifyAsync();
        _storage.Hashes[changed] = new string('b', 64);
        _storage.Free[string.Empty] = 1000;

        ArchiveImportResult result = await Import().ImportAsync(plan, null, Cancellation);

        Assert.Equal([roomy], result.Imported.Select(archive => archive.Source));
        Assert.Equal(
            [
                (changed, $"It couldn't be copied: {changed} changed since it was read."),
                (tooBig, "There isn't room for it on the library's drive."),
            ],
            result.Problems.Select(problem => (problem.Path, problem.Message)));
        Assert.Equal([SkyUi53], Assert.Single(_library.Mods).Files.Select(file => file.FileName));
    }

    [Fact]
    public async Task FiveHundredArchivesTakeTheRequestsTheScanSaid()
    {
        // 250 archives of 50 mods, named by notes, and 250 of 50 others, found by their MD5.
        for (int mod = 1; mod <= 100; mod++)
        {
            _site.AddMod($"{1000 + mod}", $"Mod {1000 + mod}");
            for (int file = 1; file <= 5; file++)
            {
                string fileId = $"{mod}0{file}";
                _site.AddFile($"{1000 + mod}", fileId, $"file-{fileId}.7z");
                string archive = Archive($"archive-{fileId}.7z");
                if (mod <= 50)
                {
                    Note(archive, $"[General]\nmodID={1000 + mod}\nfileID={fileId}\n");
                }
                else
                {
                    _lookup.ByMd5[_storage.Md5Of(archive)] = [($"{1000 + mod}", fileId)];
                }
            }
        }

        ArchiveScan scan = await Import().ScanAsync(Downloads, Game, Cancellation);
        ArchivePlan plan = await Import().IdentifyAsync(scan, null, Cancellation);

        Assert.Equal(500, scan.Archives.Count);
        Assert.Equal(350, scan.Requests);
        Assert.Equal(scan.Requests, _site.AskedFor.Count + _lookup.Searched.Count);
        Assert.Equal((500, 100), (plan.ToImport.Count, plan.NewMods));
    }

    private static ModFileRef File(string modId, string fileId) => FakeModSite.File(modId, fileId);

    private async Task<ArchivePlan> IdentifyAsync(IProgress<int>? progress = null)
    {
        ArchiveScan scan = await Import().ScanAsync(Downloads, Game, Cancellation);
        return await Import().IdentifyAsync(scan, progress, Cancellation);
    }

    private string Archive(string name, long size = 100)
    {
        string path = Path.Combine(Downloads, name);
        _storage.Add(path, size);
        return path;
    }

    private void Note(string archive, string text)
    {
        _storage.Add(archive + ".meta", text.Length);
        _storage.Contents[archive + ".meta"] = Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Puts the mod in the library, watched, with its files on disk.
    /// </summary>
    private void Have(string modId, params LibraryFile[] files)
    {
        _library.Add(LibraryMod.Restore(
            Mod(modId), modId == SkyUi ? "SkyUI" : "SkyUI Survival", null, new Uri($"https://mods.example/{modId}"), true, TestData.Now.AddDays(-9), null, files));
        foreach (LibraryFile file in files)
        {
            _storage.Add(InRoot(file.RelativePath));
        }
    }

    private string InRoot(string relativePath) => LibraryPaths.Resolve(Root, relativePath);

    private ArchiveImport Import()
        => new(new SourceRegistry([_lookup]), _library, _settings, _storage, _hold, _changes, _history.History(_time), _time, _logger);

    /// <summary>
    /// Keeps what it's told at once, rather than on the thread pool, as <see cref="Progress{T}"/> would.
    /// </summary>
    private sealed class Progress(List<int> told) : IProgress<int>
    {
        public void Report(int value) => told.Add(value);
    }
}
