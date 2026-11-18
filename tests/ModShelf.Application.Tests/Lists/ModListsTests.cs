using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Lists;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Lists;

/// <summary>
/// Exporting the library as a list and importing lists, through the real library and its downloads over a fake site.
/// </summary>
public sealed class ModListsTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string SkyUi52 = "35407";
    private const string SkyUi53 = "35408";
    private const string Skse = "30379";
    private const string Skse27 = "40001";
    private const string Skse28 = "40002";

    private static readonly SourceId Elsewhere = new("elsewhere");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _history = new();
    private readonly FakeModSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingLogger<ModLists> _logger = new();
    private readonly List<IDisposable> _disposables = [];
    private readonly SettingsService _settings;

    public ModListsTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _repository, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, SkyUi52, "SkyUI_5_2_SE.7z", "SkyUI 5.2", "5.2", replacedBy: SkyUi53, uploadedAt: TestData.Now.AddDays(-30));
        _site.AddFile(SkyUi, SkyUi53, "SkyUI_5_3_SE.7z", "SkyUI 5.3", "5.3");
        _site.AddMod(Skse, "SKSE");
        _site.AddFile(Skse, Skse27, "skse_2_7.7z", "SKSE 2.7", "2.7", category: ModFileCategory.Optional);
        _site.AddFile(Skse, Skse28, "skse_2_8.7z", "SKSE 2.8", "2.8");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        _settings.Dispose();
    }

    [Fact]
    public async Task ExportsEachModWithItsFilesButTheArchivedOldVersions()
    {
        _repository.Add(LibraryMod.Restore(
            Mod(SkyUi), "SkyUI", "schlangster", new Uri("https://mods.example/12604"), isWatched: true, TestData.Now, null,
            [Stored(SkyUi, SkyUi53, "SkyUI 5.3", "5.3"), Stored(SkyUi, SkyUi52, "SkyUI 5.2", "5.2", "_archive/SkyUI_5_2_SE.7z")],
            isPinned: true,
            note: "Needs SKSE."));
        _repository.Add(LibraryMod.Watch(Mod(Skse), "SKSE", null, null, TestData.Now));

        ModList list = await Lists().ExportAsync(ListScope.Library, Cancellation);

        Assert.Equal((null, TestData.Now), (list.Title, list.CreatedAt));
        Assert.Equal(["SKSE", "SkyUI"], list.Mods.Select(mod => mod.Name));
        ListedMod skyUi = list.Mods.Single(mod => mod.Mod == Mod(SkyUi));
        Assert.Equal(
            ("schlangster", new Uri("https://mods.example/12604"), true, true, "Needs SKSE."),
            (skyUi.Author, skyUi.PageUrl, skyUi.Watched, skyUi.Pinned, skyUi.Note));
        ListedFile file = Assert.Single(skyUi.Files);
        Assert.Equal(new ListedFile(File(SkyUi, SkyUi53), "SkyUI 5.3", "5.3", ModFileCategory.Main, Optional: false), file);
        Assert.Empty(list.Mods.Single(mod => mod.Mod == Mod(Skse)).Files);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task ExportsOnlyWhatTheScopeTakes()
    {
        ModRef elsewhere = new(new GameRef(Elsewhere, "fallout4"), "1");
        _repository.Add(LibraryMod.Watch(Mod(SkyUi), "SkyUI", null, null, TestData.Now));
        _repository.Add(LibraryMod.Watch(Mod(Skse), "SKSE", null, null, TestData.Now));
        _repository.Add(LibraryMod.Watch(elsewhere, "Fallout mod", null, null, TestData.Now));

        ModList game = await Lists().ExportAsync(new ListScope(Game), Cancellation);
        ModList chosen = await Lists().ExportAsync(new ListScope(Mods: [Mod(Skse), elsewhere]), Cancellation);
        ModList both = await Lists().ExportAsync(new ListScope(Game, [Mod(Skse), elsewhere]), Cancellation);

        Assert.Equal(["SKSE", "SkyUI"], game.Mods.Select(mod => mod.Name));
        Assert.Equal(["Fallout mod", "SKSE"], chosen.Mods.Select(mod => mod.Name));
        Assert.Equal(Mod(Skse), Assert.Single(both.Mods).Mod);
    }

    [Fact]
    public async Task ReadsBackWhatItWrites()
    {
        ModLists lists = Lists();
        ModList list = new(
            "My setup",
            TestData.Now,
            [
                new ListedMod(Mod(SkyUi), "SkyUI", "schlangster", new Uri("https://mods.example/12604"), true, true, "A note\nof two lines.",
                    [new ListedFile(File(SkyUi, SkyUi53), "SkyUI 5.3", "5.3", ModFileCategory.Main, false)]),
                new ListedMod(Mod(Skse), "SKSE", null, null, false, false, null,
                    [new ListedFile(File(Skse, Skse27), "SKSE 2.7", null, ModFileCategory.Optional, true)]),
            ]);

        ModList read = lists.Read(lists.Write(list));

        Assert.Equal((list.Title, list.CreatedAt), (read.Title, read.CreatedAt));
        Assert.Equal(list.Mods.Count, read.Mods.Count);
        for (int index = 0; index < list.Mods.Count; index++)
        {
            Assert.Equal(list.Mods[index] with { Files = [] }, read.Mods[index] with { Files = [] });
            Assert.Equal(list.Mods[index].Files, read.Mods[index].Files);
        }
    }

    [Fact]
    public void ReadsLenientlyWhatALaterVersionOrAPersonMightWrite()
    {
        ModList read = Lists().Read(
            """
            {
              "format": "modshelf-list", "version": 1, "comment": "unknown fields are passed over",
              "mods": [
                { "site": "fake-mods", "game": "skyrimspecialedition", "mod": 12604, "files": [ { "file": 35407 }, { "file": "35407" } ] },
                { "site": "fake-mods", "game": "skyrimspecialedition", "mod": "12604", "name": "SkyUI", "files": [ { "file": "35408", "category": "Old-Version" } ] },
                { "site": "fake-mods", "game": "skyrimspecialedition", "mod": "30379", "page": "ftp://mods.example/30379", "files": [ { "file": "40001", "category": "something new" } ], },
              ],
            }
            """);

        Assert.Equal((null, TestData.Now), (read.Title, read.CreatedAt));
        Assert.Equal(2, read.Mods.Count);
        ListedMod skyUi = read.Mods[0];
        Assert.Equal(("12604", true, false), (skyUi.Name, skyUi.Watched, skyUi.Pinned));
        Assert.Equal(
            [(File(SkyUi, SkyUi52), "35407", ModFileCategory.Unknown), (File(SkyUi, SkyUi53), "35408", ModFileCategory.OldVersion)],
            skyUi.Files.Select(file => (file.File, file.Name, file.Category)));
        Assert.Null(read.Mods[1].PageUrl);
        Assert.Equal(ModFileCategory.Unknown, Assert.Single(read.Mods[1].Files).Category);
    }

    [Theory]
    [InlineData("not json", "isn't valid JSON")]
    [InlineData("[]", "isn't a JSON object")]
    [InlineData("""{"format": "other", "version": 1}""", "\"format\" isn't \"modshelf-list\"")]
    [InlineData("""{"format": "modshelf-list"}""", "doesn't say its version")]
    [InlineData("""{"format": "modshelf-list", "version": 2}""", "made by a newer ModShelf (list version 2)")]
    [InlineData("""{"format": "modshelf-list", "version": 1, "mods": {}}""", "\"mods\" isn't a list")]
    [InlineData("""{"format": "modshelf-list", "version": 1, "mods": [1]}""", "Mod 1 of the list isn't an object")]
    [InlineData("""{"format": "modshelf-list", "version": 1, "mods": [{"site": "fake-mods", "game": "g"}]}""", "Mod 1 of the list doesn't say its \"mod\"")]
    [InlineData("""{"format": "modshelf-list", "version": 1, "mods": [{"site": "Fake Mods", "game": "g", "mod": "1"}]}""", "Mod 1 of the list can't be read")]
    [InlineData("""{"format": "modshelf-list", "version": 1, "mods": [{"site": "fake-mods", "game": "g", "mod": "1", "files": [{}]}]}""", "File 1 of mod 1 of the list doesn't say which file it is")]
    public void RefusesWhatIsntAListItCanRead(string text, string said)
    {
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => Lists().Read(text));

        Assert.Contains(said, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlansFromTheLibraryAloneWithoutAskingTheSite()
    {
        ModRef elsewhere = new(new GameRef(Elsewhere, "fallout4"), "1");
        _repository.Add(LibraryMod.Watch(Mod(SkyUi), "SkyUI", null, null, TestData.Now).WithFile(Stored(SkyUi, SkyUi53, "SkyUI 5.3", "5.3")));
        ModList list = List(
            Listed(SkyUi, "SkyUI", files: [MainFile(SkyUi, SkyUi53), MainFile(SkyUi, SkyUi52)]),
            Listed(Skse, "SKSE", files: [MainFile(Skse, Skse28), OptionalFile(Skse, Skse27)]),
            new ListedMod(elsewhere, "Elsewhere", null, null, true, false, null, [new ListedFile(new ModFileRef(elsewhere, "2"), "2", null, ModFileCategory.Main, false)]));

        ListImportPlan plan = await Lists().PlanAsync(list, Cancellation);

        Assert.Equal(
            [PlannedModState.InLibrary, PlannedModState.New, PlannedModState.NoSite],
            plan.Mods.Select(mod => mod.State));
        Assert.Equal([true, false], plan.Mods[0].Files.Select(file => file.InLibrary));
        Assert.Equal((1, 1, 1), (plan.NewMods, plan.ModsInLibrary, plan.ModsWithoutSite));
        Assert.Equal((2, 3, 1, 1), (plan.FilesToDownload(includeOptional: false), plan.FilesToDownload(includeOptional: true), plan.FilesInLibrary, plan.OptionalFiles));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task WatchesTheNewModsWithTheirPinAndNoteAndQueuesTheirFiles()
    {
        ModLists lists = Lists();
        ListImportPlan plan = await lists.PlanAsync(
            List(
                Listed(SkyUi, "SkyUI", pinned: true, note: "Needs SKSE.", files: [MainFile(SkyUi, SkyUi53)]),
                Listed(Skse, "SKSE", watched: false, files: [MainFile(Skse, Skse28)])),
            Cancellation);
        List<int> progress = [];

        ListImportResult result = await lists.ImportAsync(plan, new ListImportOptions(), new SynchronousProgress(progress), null, Cancellation);

        Assert.Equal((1, 2, 0), (result.ModsAdded, result.FilesQueued, result.FilesInLibrary));
        Assert.Empty(result.Problems);
        Assert.Equal([1, 2], progress);
        LibraryMod skyUi = Assert.Single(_repository.Mods);
        Assert.Equal((Mod(SkyUi), true, true, "Needs SKSE."), (skyUi.Mod, skyUi.IsWatched, skyUi.IsPinned, skyUi.Note));
        Assert.Equal([File(SkyUi, SkyUi53), File(Skse, Skse28)], _downloads.Enqueued.Select(job => job.File));
    }

    [Fact]
    public async Task QueuesThroughTheHostsOwnQueueWhenGivenOne()
    {
        ModLists lists = Lists();
        ListImportPlan plan = await lists.PlanAsync(List(Listed(SkyUi, "SkyUI", files: [MainFile(SkyUi, SkyUi53), MainFile(SkyUi, SkyUi52)])), Cancellation);
        List<Uri> asked = [];

        ListImportResult result = await lists.ImportAsync(
            plan,
            new ListImportOptions(),
            null,
            (link, _) =>
            {
                asked.Add(link);
                return Task.FromResult(asked.Count == 1);
            },
            Cancellation);

        Assert.Equal([Link(SkyUi, SkyUi53), Link(SkyUi, SkyUi52)], asked);
        Assert.Equal([File(SkyUi, SkyUi53)], result.Queued);
        Assert.Equal((1, 1), (result.FilesQueued, result.FilesInLibrary));
        Assert.Empty(_downloads.Enqueued);
    }

    [Fact]
    public async Task KeepsALibraryModsOwnChoicesAndQueuesOnlyTheFilesItLacks()
    {
        _repository.Add(LibraryMod.Restore(Mod(SkyUi), "SkyUI", null, null, isWatched: false, TestData.Now, null, [Stored(SkyUi, SkyUi53, "SkyUI 5.3", "5.3")]));
        ModLists lists = Lists();
        ListImportPlan plan = await lists.PlanAsync(
            List(Listed(SkyUi, "SkyUI", pinned: true, note: "From the list.", files: [MainFile(SkyUi, SkyUi53), MainFile(SkyUi, SkyUi52)])),
            Cancellation);

        ListImportResult result = await lists.ImportAsync(plan, new ListImportOptions(), null, null, Cancellation);

        Assert.Equal((0, 1, 1), (result.ModsAdded, result.FilesQueued, result.FilesInLibrary));
        LibraryMod skyUi = Assert.Single(_repository.Mods);
        Assert.Equal((false, false, (string?)null), (skyUi.IsWatched, skyUi.IsPinned, skyUi.Note));
        Assert.Equal(File(SkyUi, SkyUi52), Assert.Single(_downloads.Enqueued).File);
    }

    [Fact]
    public async Task WatchingOnlyWatchesEveryNewModAndQueuesNothing()
    {
        ModLists lists = Lists();
        ListImportPlan plan = await lists.PlanAsync(
            List(Listed(SkyUi, "SkyUI", files: [MainFile(SkyUi, SkyUi53)]), Listed(Skse, "SKSE", watched: false, files: [MainFile(Skse, Skse28)])),
            Cancellation);

        ListImportResult result = await lists.ImportAsync(plan, new ListImportOptions(Download: false), null, null, Cancellation);

        Assert.Equal((2, 0, 0), (result.ModsAdded, result.FilesQueued, result.FilesInLibrary));
        Assert.Equal([Mod(SkyUi), Mod(Skse)], _repository.Mods.Select(mod => mod.Mod).OrderBy(mod => mod.Id, StringComparer.Ordinal));
        Assert.All(_repository.Mods, mod => Assert.True(mod.IsWatched));
        Assert.Empty(_downloads.Enqueued);
        Assert.DoesNotContain("GetFilesAsync", _site.Asked);
    }

    [Fact]
    public async Task SaysAFileTheSiteNoLongerOffersOrTakesTheNewestMainFileOnce()
    {
        ModList list = List(Listed(SkyUi, "SkyUI", files: [MainFile(SkyUi, "1"), MainFile(SkyUi, "2")]));
        ModLists lists = Lists();

        ListImportResult said = await lists.ImportAsync(await lists.PlanAsync(list, Cancellation), new ListImportOptions(), null, null, Cancellation);
        ListImportResult replaced = await lists.ImportAsync(
            await lists.PlanAsync(list, Cancellation), new ListImportOptions(NewestForMissing: true), null, null, Cancellation);

        Assert.Equal(
            [(File(SkyUi, "1"), ListImportProblemKind.FileGone), (File(SkyUi, "2"), ListImportProblemKind.FileGone)],
            said.Problems.Select(problem => (problem.File, problem.Kind)));
        Assert.Equal((1, 0), (replaced.FilesQueued, replaced.Problems.Count));
        Assert.Equal(File(SkyUi, SkyUi53), Assert.Single(_downloads.Enqueued).File);
    }

    [Fact]
    public async Task DownloadsTheOptionalFilesOnlyWhenAsked()
    {
        ModList list = List(Listed(Skse, "SKSE", files: [MainFile(Skse, Skse28), OptionalFile(Skse, Skse27)]));
        ModLists lists = Lists();

        ListImportResult without = await lists.ImportAsync(await lists.PlanAsync(list, Cancellation), new ListImportOptions(), null, null, Cancellation);
        ListImportResult with = await lists.ImportAsync(
            await lists.PlanAsync(list, Cancellation), new ListImportOptions(IncludeOptional: true), null, null, Cancellation);

        // The main file is still queued from the first import, which counts as queued again.
        Assert.Equal((1, 2), (without.FilesQueued, with.FilesQueued));
        Assert.Equal([File(Skse, Skse28), File(Skse, Skse27)], _downloads.Enqueued.Select(job => job.File).Distinct());
    }

    [Fact]
    public async Task SaysWhatItCouldntDoAndGoesOnWithTheRest()
    {
        ModRef elsewhere = new(new GameRef(Elsewhere, "fallout4"), "1");
        _site.AddMod("500", "Gone mod");
        _site.ModFailures["500"] = new HttpRequestException("The site is down.", null, HttpStatusCode.ServiceUnavailable);
        ModLists lists = Lists();
        ListImportPlan plan = await lists.PlanAsync(
            List(
                new ListedMod(elsewhere, "Fallout mod", null, null, true, false, null, []),
                Listed("500", "Gone mod", files: [MainFile("500", "501")]),
                Listed(SkyUi, "SkyUI", files: [MainFile(SkyUi, SkyUi53)])),
            Cancellation);

        ListImportResult result = await lists.ImportAsync(plan, new ListImportOptions(), null, null, Cancellation);

        Assert.Equal(
            [
                (elsewhere, "Fallout mod", ListImportProblemKind.NoSite, (string?)null),
                (Mod("500"), "Gone mod", ListImportProblemKind.SiteFailed, "The site is down."),
            ],
            result.Problems.Select(problem => (problem.Mod, problem.ModName, problem.Kind, problem.Detail)));
        Assert.Equal((1, 1), (result.ModsAdded, result.FilesQueued));
        Assert.Equal(File(SkyUi, SkyUi53), Assert.Single(_downloads.Enqueued).File);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("Couldn't import", StringComparison.Ordinal));
    }

    private static ModList List(params ListedMod[] mods) => new(null, TestData.Now, mods);

    private static ListedMod Listed(string modId, string name, bool watched = true, bool pinned = false, string? note = null, ListedFile[]? files = null)
        => new(Mod(modId), name, null, null, watched, pinned, note, files ?? []);

    private static ListedFile MainFile(string modId, string fileId) => new(File(modId, fileId), fileId, null, ModFileCategory.Main, false);

    private static ListedFile OptionalFile(string modId, string fileId) => new(File(modId, fileId), fileId, null, ModFileCategory.Optional, true);

    private static LibraryFile Stored(string modId, string fileId, string name, string version, string? relativePath = null)
        => new(File(modId, fileId), name, version, ModFileCategory.Main, TestData.Now, relativePath ?? $"{name}.7z", 1, TestData.Now);

    private ModLists Lists()
    {
        SourceRegistry registry = new([_site]);
        ModLibrary library = new(registry, _repository, _settings, _storage, _downloads, _downloads, _changes, _history.History(_time), _time, new RecordingLogger<ModLibrary>());
        LibraryDownloads downloads = new(registry, _downloads, _repository, _settings, _storage, _changes, _history.History(_time), _time, new RecordingLogger<LibraryDownloads>());
        _disposables.Add(downloads);
        return new ModLists(library, downloads, registry, _time, _logger);
    }

    /// <summary>
    /// Records what it's told as it's told, where <see cref="Progress{T}"/> would post it later.
    /// </summary>
    private sealed class SynchronousProgress(List<int> told) : IProgress<int>
    {
        public void Report(int value) => told.Add(value);
    }
}
