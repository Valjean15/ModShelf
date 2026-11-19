using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Cleanup;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Cleanup;

/// <summary>
/// Freeing up space against a site whose SkyUI offers 5.2 (35407, an old version) and 5.3 (35408), lists 5.1 (35406)
/// as archived, and no longer lists 5.0 (35405); and whose SkyUI Survival offers one file. The site lists each at 100
/// bytes, and the library's files are that size on disk unless a test says otherwise. The library's folders follow the
/// default pattern.
/// </summary>
public sealed class LibraryCleanupTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string Survival = "3863";
    private const string SkyUiFolder = "fake-mods/skyrimspecialedition/SkyUI (12604)";
    private const string SurvivalFolder = "fake-mods/skyrimspecialedition/SkyUI Survival (3863)";
    private const string ListAgain = "List what could go again to see it as it is now.";

    private static readonly string Same = new('a', 64);

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _history = new();
    private readonly FakeModSite _site = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly FakeLibraryHold _hold = new();
    private readonly RecordingLogger<LibraryCleanup> _logger = new();
    private readonly SettingsService _settings;

    public LibraryCleanupTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, "35406", "SkyUI_5_1_SE.7z", "SkyUI 5.1", "5.1", category: ModFileCategory.Archived);
        _site.AddFile(SkyUi, "35407", "SkyUI_5_2_SE.7z", "SkyUI 5.2", "5.2", category: ModFileCategory.OldVersion);
        _site.AddFile(SkyUi, "35408", "SkyUI_5_3_SE.7z", "SkyUI 5.3", "5.3");
        _site.AddMod(Survival, "SkyUI Survival");
        _site.AddFile(Survival, "1", "Survival.7z", "Survival");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task AnArchivedOldVersionTheSiteOffersCanGoAndOneItDoesntIsKept()
    {
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35405", $"{SkyUiFolder}/_archive/SkyUI_5_0_SE.7z"),
            Stored(SkyUi, "35406", $"{SkyUiFolder}/_archive/SkyUI_5_1_SE.7z"),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [
                (CleanupGroup.Archived, "35405", (KeptBecause?)KeptBecause.NotOffered),
                (CleanupGroup.Archived, "35406", KeptBecause.NotOffered),
                (CleanupGroup.Archived, "35407", null),
            ],
            plan.Entries.Select(entry => (entry.Group, entry.File!.File.Id, entry.Kept)));
        CleanupEntry entry = plan.Entries[2];
        Assert.Equal(
            (InRoot($"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z", 100L, Mod(SkyUi), "SkyUI"),
            (entry.Path, entry.RelativePath, entry.FileName, entry.SizeInBytes, entry.Mod, entry.ModName));
        Assert.Equal((100L, 1, (GameRef?)null), (plan.CanFree, plan.Asked, plan.Game));
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
        Assert.Equal(["Information: Listed what could go from the library: 1 files of 100 bytes, and 2 kept, asking about 1 mods "], _logger.Entries);
    }

    [Fact]
    public async Task OfTwoCopiesTheOneInTheArchiveGoesWithoutAskingTheSite()
    {
        // As a file the author uploaded again under a new ID: the old one was archived when the new one came.
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z", Same),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z", Same));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        CleanupEntry entry = Assert.Single(plan.Entries);
        Assert.Equal((CleanupGroup.Duplicates, "35407", true), (entry.Group, entry.File!.File.Id, entry.CanGo));
        Assert.Equal(
            (InRoot($"{SkyUiFolder}/SkyUI_5_3_SE.7z"), Mod(SkyUi), "SkyUI", "35408"),
            (entry.SameAs!.Path, entry.SameAs.Mod, entry.SameAs.ModName, entry.SameAs.File.File.Id));
        Assert.Empty(_site.Asked);
        Assert.Equal(0, plan.Asked);
    }

    [Fact]
    public async Task OfDuplicatesAWatchedModsCopyIsKeptThenTheOneDownloadedLast()
    {
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35407", $"{SkyUiFolder}/SkyUI_5_2_SE.7z", Same, downloadedDaysAgo: 3),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z", Same, downloadedDaysAgo: 2));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z", Same, downloadedDaysAgo: 1));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [("35407", "35408"), ("1", "35408")],
            plan.Entries.Select(entry => (entry.File!.File.Id, entry.SameAs!.File.File.Id)));
        Assert.All(plan.Entries, entry => Assert.Equal((CleanupGroup.Duplicates, true), (entry.Group, entry.CanGo)));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task FilesOfDifferentSizesWithoutAHashOrEmptyArentDuplicates()
    {
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35405", $"{SkyUiFolder}/SkyUI_5_0_SE.7z", new string('e', 64)),
            Stored(SkyUi, "35406", $"{SkyUiFolder}/SkyUI_5_1_SE.7z", new string('e', 64)),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/SkyUI_5_2_SE.7z", Same),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z", Same));
        Have(Survival, watched: true, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"), Stored(Survival, "2", $"{SurvivalFolder}/Survival2.7z"));
        _storage.Add(InRoot($"{SkyUiFolder}/SkyUI_5_0_SE.7z"), 0);
        _storage.Add(InRoot($"{SkyUiFolder}/SkyUI_5_1_SE.7z"), 0);
        _storage.Add(InRoot($"{SkyUiFolder}/SkyUI_5_3_SE.7z"), 200);

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Empty(plan.Entries);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task FilesOfAModNoLongerWatchedCanGoWhenTheSiteOffersThem()
    {
        Have(
            SkyUi,
            watched: false,
            Stored(SkyUi, "35405", $"{SkyUiFolder}/SkyUI_5_0_SE.7z"),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [
                (CleanupGroup.Archived, "35407", (KeptBecause?)null),
                (CleanupGroup.Unwatched, "35405", KeptBecause.NotOffered),
                (CleanupGroup.Unwatched, "35408", null),
                (CleanupGroup.Unwatched, "1", null),
            ],
            plan.Entries.Select(entry => (entry.Group, entry.File!.File.Id, entry.Kept)));
        Assert.Equal(300, plan.CanFree);
        Assert.Equal(["35408", "1"], plan.CanGoIn(CleanupGroup.Unwatched).Select(entry => entry.File!.File.Id));
        Assert.Equal(["GetFilesAsync 12604", "GetFilesAsync 3863"], _site.AskedFor);
    }

    [Fact]
    public async Task AModGoneFromItsSiteIsntAskedAndItsFilesAreKept()
    {
        Have(
            SkyUi,
            watched: true,
            goneSince: TestData.Now.AddDays(-5),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        _storage.Add(InRoot($"{SkyUiFolder}/Readme.txt"));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [(CleanupGroup.Archived, "SkyUI_5_2_SE.7z", (KeptBecause?)KeptBecause.ModGone), (CleanupGroup.Unmatched, "Readme.txt", KeptBecause.ModGone)],
            plan.Entries.Select(entry => (entry.Group, entry.FileName, entry.Kept)));
        Assert.Empty(_site.Asked);
        Assert.Equal(0, plan.CanFree);
    }

    [Fact]
    public async Task ASiteThatRefusesKeepsTheFilesAndIsntAskedAgain()
    {
        Have(SkyUi, watched: true, Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        _site.Failures[nameof(FakeModSite.GetFilesAsync)] = new HttpRequestException("Slow down.", null, HttpStatusCode.TooManyRequests);

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [
                (CleanupGroup.Archived, "35407", (KeptBecause?)KeptBecause.NotChecked, "Slow down."),
                (CleanupGroup.Unwatched, "1", KeptBecause.NotChecked, "Not asked: fake-mods refused an earlier question. Slow down."),
            ],
            plan.Entries.Select(entry => (entry.Group, entry.File!.File.Id, entry.Kept, entry.KeptDetail)));
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
        Assert.Equal(1, plan.Asked);
        Assert.StartsWith(
            "Warning: Couldn't ask the site which files of mod 12604 of skyrimspecialedition it offers System.Net.Http.HttpRequestException: Slow down.",
            _logger.Entries.First(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherProblemWithTheSiteKeepsOnlyItsModsFiles()
    {
        Have(SkyUi, watched: true, Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        _site.ModFailures[SkyUi] = new HttpRequestException("The site is down.", null, HttpStatusCode.ServiceUnavailable);

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [("35407", (KeptBecause?)KeptBecause.NotChecked, "The site is down."), ("1", null, null)],
            plan.Entries.Select(entry => (entry.File!.File.Id, entry.Kept, entry.KeptDetail)));
        Assert.Equal(["GetFilesAsync 12604", "GetFilesAsync 3863"], _site.AskedFor);
    }

    [Fact]
    public async Task AFileInAModsFolderThatMatchesNoneOfItsFilesIsUnmatchedAndOneThatMatchesIsKept()
    {
        Have(SkyUi, watched: true, Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        _storage.Add(InRoot($"{SkyUiFolder}/Readme.txt"), 40);
        _storage.Add(InRoot($"{SkyUiFolder}/SkyUI_5_2_SE.7z"));

        // A second copy of a file the library has matches nothing a rescan would add, as a rescan has it.
        _storage.Add(InRoot($"{SkyUiFolder}/_archive/SkyUI_5_3_SE.7z"));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [
                ($"{SkyUiFolder}/Readme.txt", 40L, (KeptBecause?)null, (string?)null),
                ($"{SkyUiFolder}/SkyUI_5_2_SE.7z", 100L, KeptBecause.MatchesSiteFile, "SkyUI 5.2"),
                ($"{SkyUiFolder}/_archive/SkyUI_5_3_SE.7z", 100L, null, null),
            ],
            plan.Entries.Select(entry => (entry.RelativePath, entry.SizeInBytes, entry.Kept, entry.KeptDetail)));
        Assert.All(plan.Entries, entry => Assert.Equal((CleanupGroup.Unmatched, (LibraryFile?)null, Mod(SkyUi), "SkyUI"), (entry.Group, entry.File, entry.Mod, entry.ModName)));
        Assert.Equal(["GetFilesAsync 12604"], _site.AskedFor);
    }

    [Fact]
    public async Task AFileInAFolderModsShareIsUnmatchedOnlyWhenItsNoneOfTheirs()
    {
        _store.Settings = new AppSettings { LibraryFolders = FolderPattern.Parse("{source}/{game}/{file}") };
        Have(SkyUi, watched: true, Stored(SkyUi, "35408", "fake-mods/skyrimspecialedition/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: true, Stored(Survival, "2", "fake-mods/skyrimspecialedition/Survival2.7z"));
        _storage.Add(InRoot("fake-mods/skyrimspecialedition/Survival.7z"));
        _storage.Add(InRoot("fake-mods/skyrimspecialedition/Readme.txt"));

        CleanupPlan plan = await Cleanup().PlanAsync(null, null, Cancellation);

        Assert.Equal(
            [("Readme.txt", "SkyUI", (KeptBecause?)null, (string?)null), ("Survival.7z", "SkyUI Survival", KeptBecause.MatchesSiteFile, "Survival")],
            plan.Entries.Select(entry => (entry.FileName, entry.ModName, entry.Kept, entry.KeptDetail)));
        Assert.Equal(["GetFilesAsync 12604", "GetFilesAsync 3863"], _site.AskedFor);
    }

    [Fact]
    public async Task ForOneGameOnlyItsFilesAreListedThoughTheCopyKeptMayBeAnothersAndFilesNotOnDiskNeverAre()
    {
        ModRef other = new(new GameRef(FakeModSite.SourceId, "skyrim"), "99");
        _library.Add(LibraryMod.Restore(
            other, "Other", null, null, true, TestData.Now.AddDays(-9), null,
            [
                new LibraryFile(new ModFileRef(other, "1"), "Other", null, ModFileCategory.Main, TestData.Now.AddDays(-9), "fake-mods/skyrim/Other (99)/Other.7z", 100, TestData.Now, Same),
                new LibraryFile(new ModFileRef(other, "2"), "Older", null, ModFileCategory.Main, TestData.Now.AddDays(-9), "fake-mods/skyrim/Other (99)/_archive/Older.7z", 100, TestData.Now, null),
            ]));
        _storage.Add(InRoot("fake-mods/skyrim/Other (99)/Other.7z"));
        _storage.Add(InRoot("fake-mods/skyrim/Other (99)/_archive/Older.7z"));
        _storage.Add(InRoot("fake-mods/skyrim/Other (99)/Readme.txt"));
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35406", $"{SkyUiFolder}/_archive/SkyUI_5_1_SE.7z"),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z", Same),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        _storage.Remove(InRoot($"{SkyUiFolder}/_archive/SkyUI_5_1_SE.7z"));

        CleanupPlan plan = await Cleanup().PlanAsync(Game, null, Cancellation);

        CleanupEntry entry = Assert.Single(plan.Entries);
        Assert.Equal((CleanupGroup.Duplicates, "35407", InRoot("fake-mods/skyrim/Other (99)/Other.7z")), (entry.Group, entry.File!.File.Id, entry.SameAs!.Path));
        Assert.Equal(Game, plan.Game);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task TellsHowManyModsAreAskedAsEachIs()
    {
        Have(SkyUi, watched: false, Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        RecordingProgress progress = new();

        await Cleanup().PlanAsync(null, progress, Cancellation);

        Assert.Equal([new CleanupProgress(0, 2), new CleanupProgress(1, 2), new CleanupProgress(2, 2)], progress.Reports);
    }

    [Fact]
    public async Task DeletesTheChosenFilesFromDiskAndTheLibraryWhileHoldingItAndRecordsEach()
    {
        Have(SkyUi, watched: true, Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        _storage.Add(InRoot($"{SkyUiFolder}/_archive/Readme.txt"), 40);
        LibraryCleanup cleanup = Cleanup();
        CleanupPlan plan = await cleanup.PlanAsync(null, null, Cancellation);
        List<string> deleted = [];
        _storage.BeforeDelete = path =>
        {
            Assert.True(_hold.IsHeld);
            deleted.Add(path);
        };
        RecordingProgress progress = new();
        _time.Advance(TimeSpan.FromMinutes(1));

        CleanupResult result = await cleanup.DeleteAsync([.. plan.Entries.Where(entry => entry.CanGo)], progress, Cancellation);

        Assert.Equal(plan.Entries, result.Deleted);
        Assert.Empty(result.Failures);
        Assert.Equal(240, result.Freed);
        Assert.Equal(
            [InRoot($"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), InRoot($"{SurvivalFolder}/Survival.7z"), InRoot($"{SkyUiFolder}/_archive/Readme.txt")],
            deleted);
        Assert.Equal([InRoot($"{SkyUiFolder}/SkyUI_5_3_SE.7z")], _storage.Files);
        Assert.Equal(["35408"], Assert.Single(_library.Mods).Files.Select(file => file.File.Id));
        Assert.Equal([InRoot(SurvivalFolder), InRoot($"{SkyUiFolder}/_archive")], _storage.RemovedFolders);
        Assert.Equal(
            [
                (LibraryEventKind.Deleted, Mod(SkyUi), "35407", "SkyUI 5.2", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"),
                (LibraryEventKind.Deleted, Mod(Survival), "1", "Survival", $"{SurvivalFolder}/Survival.7z"),
                (LibraryEventKind.Deleted, Mod(SkyUi), (string?)null, (string?)"Readme.txt", (string?)$"{SkyUiFolder}/_archive/Readme.txt"),
            ],
            _history.Events.Select(happened => (happened.Kind, happened.Mod, happened.File?.Id, happened.FileName, happened.RelativePath)));
        Assert.All(_history.Events, happened => Assert.Equal(_time.GetUtcNow(), happened.At));
        Assert.Equal([new CleanupProgress(1, 3), new CleanupProgress(2, 3), new CleanupProgress(3, 3)], progress.Reports);
        Assert.Equal((1, false), (_hold.Holds, _hold.IsHeld));
        Assert.Equal(
            [
                "Information: Deleted Readme.txt, which matched no file of mod 12604 of skyrimspecialedition, from disk ",
                "Information: Freed up 240 bytes, deleting 3 files, and left 0 ",
            ],
            _logger.Entries.Skip(1));
    }

    [Fact]
    public async Task AFileThatChangedSinceItWasListedIsLeft()
    {
        Have(
            SkyUi,
            watched: true,
            Stored(SkyUi, "35406", $"{SkyUiFolder}/SkyUI_5_1_SE.7z", Same, downloadedDaysAgo: 3),
            Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"),
            Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z", Same));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        _storage.Add(InRoot($"{SkyUiFolder}/Readme.txt"));
        _storage.Add(InRoot($"{SkyUiFolder}/Notes.txt"));
        LibraryCleanup cleanup = Cleanup();
        CleanupPlan plan = await cleanup.PlanAsync(null, null, Cancellation);
        Assert.Equal(
            [(CleanupGroup.Archived, "SkyUI_5_2_SE.7z"), (CleanupGroup.Duplicates, "SkyUI_5_1_SE.7z"), (CleanupGroup.Unwatched, "Survival.7z"), (CleanupGroup.Unmatched, "Notes.txt"), (CleanupGroup.Unmatched, "Readme.txt")],
            plan.Entries.Select(entry => (entry.Group, entry.FileName)));

        _storage.Add(InRoot($"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), 150);
        _storage.Remove(InRoot($"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        await _library.UpdateAsync(Mod(Survival), mod => mod!.Watched(), Cancellation);
        _storage.Remove(InRoot($"{SkyUiFolder}/Notes.txt"));
        await _library.UpdateAsync(
            Mod(SkyUi),
            mod => mod!.WithFile(new LibraryFile(File(SkyUi, "9"), "Readme", null, ModFileCategory.Miscellaneous, TestData.Now, $"{SkyUiFolder}/Readme.txt", 100, TestData.Now, null)),
            Cancellation);

        CleanupResult result = await cleanup.DeleteAsync([.. plan.Entries.Where(entry => entry.CanGo)], null, Cancellation);

        Assert.Empty(result.Deleted);
        Assert.Equal(
            [
                $"It changed since it was listed. {ListAgain}",
                $"The copy kept, {InRoot($"{SkyUiFolder}/SkyUI_5_3_SE.7z")}, changed or went since it was listed. {ListAgain}",
                $"Its mod is watched again. {ListAgain}",
                "It's no longer on disk.",
                $"It's in the library now. {ListAgain}",
            ],
            result.Failures.Select(failure => failure.Message));
        Assert.Equal(plan.Entries, result.Failures.Select(failure => failure.Entry));
        Assert.Empty(_history.Events);
        Assert.Equal("Information: Freed up 0 bytes, deleting 0 files, and left 5 ", _logger.Entries.Last());
    }

    [Fact]
    public async Task AFileNoLongerWhereItWasListedIsLeft()
    {
        Have(SkyUi, watched: true, Stored(SkyUi, "35407", $"{SkyUiFolder}/_archive/SkyUI_5_2_SE.7z"), Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        Have(Survival, watched: false, Stored(Survival, "1", $"{SurvivalFolder}/Survival.7z"));
        LibraryCleanup cleanup = Cleanup();
        CleanupPlan plan = await cleanup.PlanAsync(null, null, Cancellation);
        string folder = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Elsewhere");

        // The archived file moved back out, as undoing its archiving does; and the game's folder moved.
        await _library.UpdateAsync(
            Mod(SkyUi),
            mod => mod!.WithFile(new LibraryFile(File(SkyUi, "35407"), "SkyUI 5.2", "5.2", ModFileCategory.OldVersion, TestData.Now, $"{SkyUiFolder}/SkyUI_5_2_SE.7z", 100, TestData.Now, null)),
            Cancellation);
        _store.Settings = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [new GameRef(FakeModSite.SourceId, "skyrim")] = new() { Root = folder } },
        };

        CleanupResult first = await cleanup.DeleteAsync([plan.Entries[0]], null, Cancellation);
        _store.Settings = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [Game] = new() { Root = folder } },
        };
        CleanupResult second = await cleanup.DeleteAsync([plan.Entries[1]], null, Cancellation);

        Assert.Equal($"It's no longer in the library where it was listed. {ListAgain}", Assert.Single(first.Failures).Message);
        Assert.Equal($"Its game's folder changed since it was listed. {ListAgain}", Assert.Single(second.Failures).Message);
        Assert.Equal(3, _storage.Files.Count);
    }

    [Fact]
    public async Task AFileThatCantBeDeletedIsLeftWithWhyAndTheRestGo()
    {
        Have(SkyUi, watched: false, Stored(SkyUi, "35407", $"{SkyUiFolder}/SkyUI_5_2_SE.7z"), Stored(SkyUi, "35408", $"{SkyUiFolder}/SkyUI_5_3_SE.7z"));
        string open = InRoot($"{SkyUiFolder}/SkyUI_5_2_SE.7z");
        _storage.Failures[open] = new IOException("Another program has it open.");
        LibraryCleanup cleanup = Cleanup();
        CleanupPlan plan = await cleanup.PlanAsync(null, null, Cancellation);

        CleanupResult result = await cleanup.DeleteAsync(plan.Entries, null, Cancellation);

        CleanupFailure failure = Assert.Single(result.Failures);
        Assert.Equal(("SkyUI_5_2_SE.7z", "Another program has it open."), (failure.Entry.FileName, failure.Message));
        Assert.Equal("SkyUI_5_3_SE.7z", Assert.Single(result.Deleted).FileName);
        Assert.Equal(["35407"], Assert.Single(_library.Mods).Files.Select(file => file.File.Id));
        Assert.Equal([open], _storage.Files);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith(
            "Warning: Couldn't delete SkyUI_5_2_SE.7z to free up space System.IO.IOException: Another program has it open.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AKeptFileIsntDeleted()
    {
        Have(SkyUi, watched: false, Stored(SkyUi, "35405", $"{SkyUiFolder}/SkyUI_5_0_SE.7z"));
        LibraryCleanup cleanup = Cleanup();
        CleanupPlan plan = await cleanup.PlanAsync(null, null, Cancellation);

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => cleanup.DeleteAsync(plan.Entries, null, Cancellation));

        Assert.StartsWith("SkyUI_5_0_SE.7z is kept, so it isn't deleted.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, _hold.Holds);
        Assert.Single(_storage.Files);
    }

    private static LibraryFile Stored(string modId, string fileId, string relativePath, string? sha256 = null, int downloadedDaysAgo = 2)
        => new(File(modId, fileId), $"File {fileId}", null, ModFileCategory.Main, TestData.Now.AddDays(-9), relativePath, 100, TestData.Now.AddDays(-downloadedDaysAgo), sha256);

    /// <summary>
    /// Puts the mod in the library, with its files on disk, each named on its site as the site names it.
    /// </summary>
    private void Have(string modId, bool watched, params LibraryFile[] files) => Have(modId, watched, null, files);

    private void Have(string modId, bool watched, DateTimeOffset? goneSince, params LibraryFile[] files)
    {
        _library.Add(LibraryMod.Restore(
            Mod(modId),
            modId == SkyUi ? "SkyUI" : "SkyUI Survival",
            null,
            new Uri($"https://mods.example/{modId}"),
            watched,
            TestData.Now.AddDays(-9),
            null,
            files.Select(file => new LibraryFile(
                file.File, SiteName(modId, file.File.Id), file.VersionLabel, file.Category, file.UploadedAt, file.RelativePath, file.SizeInBytes, file.DownloadedAt, file.Sha256)),
            goneSince: goneSince));
        foreach (LibraryFile file in files)
        {
            _storage.Add(InRoot(file.RelativePath));
        }
    }

    private string SiteName(string modId, string fileId)
    {
        try
        {
            return _site.FileOf(modId, fileId).Name;
        }
        catch (InvalidOperationException)
        {
            return $"File {fileId}";
        }
    }

    private string InRoot(string relativePath) => LibraryPaths.Resolve(Root, relativePath);

    private LibraryCleanup Cleanup()
    {
        RecordingDownloadService downloads = new();
        LibraryHistory history = _history.History(_time);
        ModLibrary library = new(
            new SourceRegistry([_site]), _library, _settings, _storage, downloads, downloads, new LibraryChanges(), history, _time, NullLogger<ModLibrary>.Instance);
        return new LibraryCleanup(new SourceRegistry([_site]), _library, _settings, _storage, library, _hold, history, _time, _logger);
    }

    private sealed class RecordingProgress : IProgress<CleanupProgress>
    {
        public List<CleanupProgress> Reports { get; } = [];

        public void Report(CleanupProgress value) => Reports.Add(value);
    }
}
