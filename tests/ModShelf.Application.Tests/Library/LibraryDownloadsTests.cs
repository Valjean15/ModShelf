using System.Collections.Immutable;
using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Library;

public sealed class LibraryDownloadsTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string SkyUi52 = "35407";
    private const string SkyUi53 = "35408";

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly FakeModSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeUserFolders _folders = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<LibraryDownloads> _logger = new();
    private readonly SettingsService _settings;

    public LibraryDownloadsTests()
    {
        _settings = new SettingsService(_store, _folders, _library, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, SkyUi52, "SkyUI_5_2_SE.7z", "SkyUI 5.2", "5.2");
        _site.AddFile(SkyUi, SkyUi53, "SkyUI_5_3_SE.7z", "SkyUI 5.3", "5.3");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    private string SkyUiFolder => InRoot("fake-mods", "skyrimspecialedition", "SkyUI (12604)");

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task QueuesANewFileWhereItsGamesPatternSays()
    {
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        string expected = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z");
        Assert.Equal(expected, added.Path);
        DownloadJobSnapshot job = Assert.Single(_downloads.Enqueued);
        Assert.Same(job, added.Queued!.Job);
        Assert.Equal((File(SkyUi, SkyUi52), SkyUiFolder, "SkyUI_5_2_SE.7z"), (job.File, job.TargetDirectory, job.FileName));
        Assert.Equal(["GetFilesAsync", "GetModAsync", "RecognizeAsync"], _site.Asked.Order());
        Assert.Empty(_library.Mods);
    }

    [Fact]
    public async Task UsesTheGamesOwnPatternAndTheRootSet()
    {
        string root = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Mods");
        _store.Settings = new AppSettings
        {
            LibraryRoot = root,
            LibraryFolders = FolderPattern.Parse("everything/{file}"),
            GameFolders = ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                [KeyValuePair.Create("SkyrimSpecialEdition", FolderPattern.Parse("Skyrim/{mod}/{file}"))]),
        };
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(Path.Combine(root, "Skyrim", "SkyUI", "SkyUI_5_2_SE.7z"), added.Path);
    }

    [Fact]
    public async Task MakesTheNamesSafeOnEverySystem()
    {
        _site.AddMod("3863", "SKSE: Script Extender?");
        _site.AddFile("3863", "1", "CON.7z");
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link("3863", "1"), Cancellation);

        Assert.Equal(InRoot("fake-mods", "skyrimspecialedition", "SKSE_ Script Extender_ (3863)", "_CON.7z"), added.Path);
    }

    [Fact]
    public async Task PutsAModsNextFileInItsFolderEvenAfterTheSiteRenamedIt()
    {
        string folder = "fake-mods/skyrimspecialedition/SkyUI old (12604)";
        _library.Add(ModWith(StoredFile(SkyUi52, $"{folder}/SkyUI_5_2_SE.7z")));
        _site.AddMod(SkyUi, "SkyUI new");
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi53), Cancellation);

        Assert.Equal(InRoot("fake-mods", "skyrimspecialedition", "SkyUI old (12604)", "SkyUI_5_3_SE.7z"), added.Path);
    }

    public static TheoryData<string> Takers() => ["the library", "the queue", "the disk"];

    [Theory]
    [MemberData(nameof(Takers))]
    public async Task AddsTheFileIdToANameTakenAlready(string taker)
    {
        string taken = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z");
        Take(taker, taken, other: "1");
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE (35407).7z"), added.Path);
    }

    [Fact]
    public async Task TakesNamesThatDifferOnlyInCaseAsTaken()
    {
        _library.Add(ModWith(StoredFile("1", "fake-mods/skyrimspecialedition/SkyUI (12604)/SKYUI_5_2_SE.7Z")));
        _downloads.Update(RecordingDownloadService.Job(File(SkyUi, "2"), SkyUiFolder.ToUpperInvariant(), "SkyUI_5_2_SE (35407).7z", DownloadJobStatus.Paused));
        using LibraryDownloads library = Create();

        IOException refused = await Assert.ThrowsAsync<IOException>(() => library.AddAsync(Link(SkyUi, SkyUi52), Cancellation));

        Assert.Equal(
            $"There are files named SkyUI_5_2_SE.7z and SkyUI_5_2_SE (35407).7z in {SkyUiFolder} already, so there's no name left for this one.",
            refused.Message);
        Assert.Empty(_downloads.Enqueued);
    }

    [Fact]
    public async Task IgnoresFinishedDownloadsOfOtherFilesForTheName()
    {
        DownloadJobSnapshot done = RecordingDownloadService.Job(File(SkyUi, "1"), SkyUiFolder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Cancelled);
        _downloads.Update(done);
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"), added.Path);
    }

    [Fact]
    public async Task DoesntDownloadAFileTheLibraryHasOnDisk()
    {
        _library.Add(ModWith(StoredFile(SkyUi52, "kept/SkyUI.7z")));
        _storage.Add(InRoot("kept", "SkyUI.7z"));
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(new LibraryDownload(InRoot("kept", "SkyUI.7z"), null), added);
        Assert.Empty(_downloads.Changes);
        Assert.Equal(["RecognizeAsync"], _site.Asked);
    }

    [Fact]
    public async Task DownloadsAFileGoneFromDiskAgainToTheSamePlace()
    {
        _library.Add(ModWith(StoredFile(SkyUi52, "kept/SkyUI.7z")));
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(InRoot("kept", "SkyUI.7z"), added.Path);
        Assert.Equal((InRoot("kept"), "SkyUI.7z"), (added.Queued!.Job.TargetDirectory, added.Queued.Job.FileName));
        Assert.Equal(["RecognizeAsync"], _site.Asked);
    }

    [Fact]
    public async Task ContinuesAFileQueuedForTheLibraryWhereItWasGoing()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(File(SkyUi, SkyUi52), InRoot("earlier"), "named earlier.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Update(waiting);
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(new LibraryDownload(InRoot("earlier", "named earlier.7z"), new LinkDownload(waiting, AlreadyQueued: true)), added);
        Assert.Equal((File(SkyUi, SkyUi52), HandoffLink(SkyUi, SkyUi52)), Assert.Single(_downloads.HandedOff));
        Assert.Empty(_downloads.Enqueued);
        Assert.Equal(["RecognizeAsync"], _site.Asked);
    }

    [Fact]
    public async Task QueuesAFileDownloadedOutsideTheLibraryAgain()
    {
        string elsewhere = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Elsewhere");
        _downloads.Update(RecordingDownloadService.Job(File(SkyUi, SkyUi52), elsewhere, "SkyUI_5_2_SE.7z", DownloadJobStatus.Queued));
        _downloads.Update(RecordingDownloadService.Completed(RecordingDownloadService.Job(File(SkyUi, SkyUi52), elsewhere, "b.7z", DownloadJobStatus.Queued)));
        _storage.Add(Path.Combine(elsewhere, "b.7z"));
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"), added.Path);
        Assert.False(added.Queued!.AlreadyQueued);
    }

    [Fact]
    public async Task RecordsAFileDownloadedIntoTheLibraryButNotRecordedYet()
    {
        DownloadJobSnapshot done = RecordingDownloadService.Completed(
            RecordingDownloadService.Job(File(SkyUi, SkyUi52), SkyUiFolder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Queued));
        _downloads.Update(done);
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"));
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(new LibraryDownload(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"), null), added);
        Assert.Equal("fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", Assert.Single(Assert.Single(_library.Mods).Files).RelativePath);
        Assert.Empty(_downloads.Enqueued);
    }

    [Fact]
    public async Task HandsALinkThatCompletesAHandoffToTheQueue()
    {
        using LibraryDownloads library = Create();

        LibraryDownload added = await library.AddAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.Equal((File(SkyUi, SkyUi52), HandoffLink(SkyUi, SkyUi52)), Assert.Single(_downloads.HandedOff));
        Assert.Equal([nameof(IDownloadService.HandOffAsync), nameof(IDownloadService.EnqueueAsync)], _downloads.Changes);
        Assert.Equal(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"), added.Path);
    }

    [Fact]
    public async Task RefusesAFileTheSiteDoesntList()
    {
        using LibraryDownloads library = Create();

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(() => library.AddAsync(Link(SkyUi, "99"), Cancellation));

        Assert.StartsWith("The site doesn't list file 99 among the mod's files", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_downloads.Changes);
    }

    [Fact]
    public async Task RefusesAPathTooLongForTheSystem()
    {
        FakeLibraryStorage storage = new() { MaxPath = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z").Length - 1 };
        using LibraryDownloads library = Create(storage);

        await Assert.ThrowsAsync<PathTooLongException>(() => library.AddAsync(Link(SkyUi, SkyUi52), Cancellation));

        Assert.Empty(_downloads.Changes);
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        using LibraryDownloads library = Create();

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => library.AddAsync(new Uri("https://elsewhere.example/file.zip"), Cancellation));

        Assert.Equal("No source knows this link.", refused.Message);
        Assert.IsType<UnsupportedLinkException>(refused.InnerException);
    }

    [Fact]
    public async Task OnlyTakesFilesOfSitesThatDescribeTheirMods()
    {
        Uri link = new("https://elsewhere.example/file.zip");
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), link.AbsoluteUri), isCatchAll: true);
        using LibraryDownloads library = Create(sources: [direct]);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => library.AddAsync(link, Cancellation));

        Assert.Equal("This link isn't to a mod on a site whose mods ModShelf keeps in a library.", refused.Message);
        Assert.Null(refused.InnerException);
        Assert.Empty(_downloads.Changes);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        using LibraryDownloads library = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => library.AddAsync(new Uri("12604/35407", UriKind.Relative), Cancellation));

        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task RecordsAQueuedFileWithTheDetailsReadToQueueIt()
    {
        using LibraryDownloads library = Create();
        LibraryDownload added = await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation);
        _time.Advance(TimeSpan.FromMinutes(5));

        LibraryFile? recorded = await library.RecordAsync(RecordingDownloadService.Completed(added.Queued!.Job, 4321), Cancellation);

        LibraryMod mod = Assert.Single(_library.Mods);
        Assert.Same(recorded, Assert.Single(mod.Files));
        Assert.Equal(
            (File(SkyUi, SkyUi52), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now.AddDays(-1)),
            (recorded!.File, recorded.Name, recorded.VersionLabel, recorded.Category, recorded.UploadedAt));
        Assert.Equal(
            ("fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z", 4321L, TestData.Now.AddMinutes(5)),
            (recorded.RelativePath, recorded.SizeInBytes, recorded.DownloadedAt));
        Assert.Equal(("SkyUI", "Someone", true, TestData.Now.AddMinutes(5)), (mod.Name, mod.Author, mod.IsWatched, mod.AddedAt));
        Assert.Equal(3, _site.Asked.Count);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Added SkyUI_5_2_SE.7z to the library", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsTheDetailsAgainAfterARestart()
    {
        LibraryDownload added;
        using (LibraryDownloads before = Create())
        {
            added = await before.AddAsync(Link(SkyUi, SkyUi52), Cancellation);
        }

        using LibraryDownloads library = Create();
        LibraryFile? recorded = await library.RecordAsync(RecordingDownloadService.Completed(added.Queued!.Job), Cancellation);

        Assert.Equal("SkyUI 5.2", recorded!.Name);
        Assert.Equal(["GetFilesAsync", "GetFilesAsync", "GetModAsync", "GetModAsync", "RecognizeAsync"], _site.Asked.Order());
    }

    [Fact]
    public async Task AModInTheLibraryKeepsItsChoiceAndItsOtherFiles()
    {
        LibraryFile other = StoredFile(SkyUi53, "fake-mods/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z");
        _library.Add(LibraryMod.Restore(Mod(SkyUi), "SkyUI old", null, null, isWatched: false, TestData.Now.AddDays(-9), null, [other]));
        using LibraryDownloads library = Create();

        await library.RecordAsync(Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z"), Cancellation);

        LibraryMod mod = Assert.Single(_library.Mods);
        Assert.Equal(("SkyUI", "Someone", false, TestData.Now.AddDays(-9)), (mod.Name, mod.Author, mod.IsWatched, mod.AddedAt));
        Assert.Equal([SkyUi53, SkyUi52], mod.Files.Select(file => file.File.Id));
    }

    [Fact]
    public async Task RecordingTheSameDownloadAgainChangesNothing()
    {
        DownloadJobSnapshot done = Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z");
        using LibraryDownloads library = Create();
        LibraryFile? first = await library.RecordAsync(done, Cancellation);
        LibraryMod before = Assert.Single(_library.Mods);
        _time.Advance(TimeSpan.FromHours(1));

        LibraryFile? again = await library.RecordAsync(done, Cancellation);

        Assert.Same(first, again);
        Assert.Same(before, Assert.Single(_library.Mods));
        Assert.Equal(2, _site.Asked.Count);
    }

    [Fact]
    public async Task ADownloadAgainUpdatesTheFileWithoutAskingTheSite()
    {
        _library.Add(ModWith(StoredFile(SkyUi52, "kept/SkyUI.7z")));
        _time.Advance(TimeSpan.FromDays(1));
        DownloadJobSnapshot again = Done(SkyUi52, InRoot("kept"), "SkyUI.7z", size: 999) with { CreatedAt = _time.GetUtcNow() };
        using LibraryDownloads library = Create();

        LibraryFile? recorded = await library.RecordAsync(again, Cancellation);

        Assert.Equal(("SkyUI 5.2", 999L, TestData.Now.AddDays(1)), (recorded!.Name, recorded.SizeInBytes, recorded.DownloadedAt));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task RecordsOnlyFinishedDownloadsUnderTheRoot()
    {
        using LibraryDownloads library = Create();
        string elsewhere = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Elsewhere");

        Assert.Null(await library.RecordAsync(Done(SkyUi52, elsewhere, "SkyUI_5_2_SE.7z"), Cancellation));
        Assert.Null(await library.RecordAsync(Done(SkyUi52, Root + "2", "SkyUI_5_2_SE.7z"), Cancellation));
        Assert.Null(await library.RecordAsync(Done(SkyUi52, SkyUiFolder, "SkyUI.7z") with { Status = DownloadJobStatus.Downloading }, Cancellation));
        Assert.Empty(_library.Mods);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task RecordsAFileAtTheRootItself()
    {
        using LibraryDownloads library = Create();

        LibraryFile? recorded = await library.RecordAsync(Done(SkyUi52, Root, "SkyUI.7z"), Cancellation);

        Assert.Equal("SkyUI.7z", recorded!.RelativePath);
    }

    [Fact]
    public async Task ACancelledDownloadForgetsTheDetails()
    {
        using LibraryDownloads library = Create();
        DownloadJobSnapshot queued = (await library.AddAsync(Link(SkyUi, SkyUi52), Cancellation)).Queued!.Job;

        Assert.Null(await library.RecordAsync(queued with { Status = DownloadJobStatus.Cancelled }, Cancellation));
        await library.RecordAsync(RecordingDownloadService.Completed(queued), Cancellation);

        Assert.Equal(5, _site.Asked.Count);
    }

    [Fact]
    public async Task RecordsTheSameDownloadOnceWhenAskedTwiceAtOnce()
    {
        DownloadJobSnapshot done = Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z");
        using LibraryDownloads library = Create();
        _site.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<LibraryFile?> first = library.RecordAsync(done, Cancellation);
        Task<LibraryFile?> second = library.RecordAsync(done, Cancellation);
        await WaitUntilAsync(() => _site.Asked.Count == 2);
        _site.Hold.SetResult();

        Assert.Same(await first, await second);
        Assert.Equal(2, _site.Asked.Count);
    }

    [Fact]
    public async Task PassesOnWhatTheSiteRefusesWhenRecording()
    {
        _site.Failures["GetModAsync"] = new HttpRequestException("Unreachable.", null, HttpStatusCode.ServiceUnavailable);
        using LibraryDownloads library = Create();

        await Assert.ThrowsAsync<HttpRequestException>(() => library.RecordAsync(Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z"), Cancellation));

        Assert.Empty(_library.Mods);
    }

    [Fact]
    public async Task RecordsTheFinishedDownloadsTheLibraryMissed()
    {
        DownloadJobSnapshot missed = Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z");
        DownloadJobSnapshot gone = Done(SkyUi53, SkyUiFolder, "SkyUI_5_3_SE.7z");
        DownloadJobSnapshot outside = Done("1", Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Elsewhere"), "a.7z");
        DownloadJobSnapshot unfinished = Done("2", SkyUiFolder, "b.7z") with { Status = DownloadJobStatus.Paused };
        foreach (DownloadJobSnapshot job in new[] { missed, gone, outside, unfinished })
        {
            _downloads.Update(job);
            _storage.Add(Path.Combine(job.TargetDirectory, job.FileName));
        }

        _storage.Remove(Path.Combine(gone.TargetDirectory, gone.FileName));
        using LibraryDownloads library = Create();

        await library.RecordMissedAsync(Cancellation);

        Assert.Equal([SkyUi52], Assert.Single(_library.Mods).Files.Select(file => file.File.Id));
    }

    [Fact]
    public async Task LeavesTheDownloadsTheLibraryHasAlone()
    {
        DownloadJobSnapshot done = Done(SkyUi52, SkyUiFolder, "SkyUI_5_2_SE.7z");
        _downloads.Update(done);
        _storage.Add(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"));
        using (LibraryDownloads before = Create())
        {
            await before.RecordAsync(done, Cancellation);
        }

        LibraryMod recorded = Assert.Single(_library.Mods);
        using LibraryDownloads library = Create();

        await library.RecordMissedAsync(Cancellation);

        Assert.Same(recorded, Assert.Single(_library.Mods));
        Assert.Equal(2, _site.Asked.Count);
    }

    [Fact]
    public async Task AnOlderDownloadElsewhereDoesntReplaceTheLibrarysFile()
    {
        DownloadJobSnapshot older = Done(SkyUi52, InRoot("older"), "SkyUI_5_2_SE.7z") with { CreatedAt = TestData.Now.AddDays(-2) };
        _downloads.Update(older);
        _storage.Add(Path.Combine(older.TargetDirectory, older.FileName));
        _library.Add(ModWith(StoredFile(SkyUi52, "kept/SkyUI.7z")));
        LibraryMod kept = Assert.Single(_library.Mods);
        using LibraryDownloads library = Create();

        await library.RecordMissedAsync(Cancellation);
        LibraryFile? recorded = await library.RecordAsync(older, Cancellation);

        Assert.Same(kept, Assert.Single(_library.Mods));
        Assert.Equal("kept/SkyUI.7z", recorded!.RelativePath);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task RecordsTheMissedDownloadsItCanAndLogsTheOthers()
    {
        _site.AddMod("3863", "SKSE");
        _site.AddFile("3863", "1", "skse.7z");
        DownloadJobSnapshot unlisted = Done("99", SkyUiFolder, "unlisted.7z");
        DownloadJobSnapshot listed = RecordingDownloadService.Completed(
            RecordingDownloadService.Job(File("3863", "1"), InRoot("skse"), "skse.7z", DownloadJobStatus.Queued));
        foreach (DownloadJobSnapshot job in new[] { unlisted, listed })
        {
            _downloads.Update(job);
            _storage.Add(Path.Combine(job.TargetDirectory, job.FileName));
        }

        using LibraryDownloads library = Create();

        await library.RecordMissedAsync(Cancellation);

        Assert.Equal(Mod("3863"), Assert.Single(_library.Mods).Mod);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't add unlisted.7z to the library", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogsWhenItCantLookForMissedDownloads()
    {
        _store.Failure = new InvalidDataException("The settings can't be read.");
        using LibraryDownloads library = Create();

        await library.RecordMissedAsync(Cancellation);

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't look for finished downloads", StringComparison.Ordinal));
    }

    private static LibraryMod ModWith(LibraryFile file) => LibraryMod.Watch(file.File.Mod, "SkyUI", null, null, TestData.Now).WithFile(file);

    private static LibraryFile StoredFile(string fileId, string relativePath)
        => new(File(SkyUi, fileId), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now.AddDays(-1), relativePath, 100, TestData.Now);

    private static DownloadJobSnapshot Done(string fileId, string folder, string fileName, long size = 100)
        => RecordingDownloadService.Completed(RecordingDownloadService.Job(File(SkyUi, fileId), folder, fileName, DownloadJobStatus.Queued), size);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    private string InRoot(params string[] parts) => Path.Combine([Root, .. parts]);

    private void Take(string taker, string path, string other)
    {
        switch (taker)
        {
            case "the library":
                string relative = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
                _library.Add(ModWith(StoredFile(other, relative)));
                break;
            case "the queue":
                _downloads.Update(RecordingDownloadService.Job(
                    File(SkyUi, other), Path.GetDirectoryName(path)!, Path.GetFileName(path), DownloadJobStatus.Downloading));
                break;
            default:
                _storage.Add(path);
                break;
        }
    }

    private LibraryDownloads Create(FakeLibraryStorage? storage = null, IModSource[]? sources = null)
        => new(new SourceRegistry([_site, .. sources ?? []]), _downloads, _library, _settings, storage ?? _storage, _time, _logger);
}
