using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Library;

public sealed class ModLibraryTests : IDisposable
{
    private static readonly Uri Link = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _history = new();
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly FakeCatalogSource _site = new(TestData.Nexus);
    private readonly FakeLibraryStorage _storage = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly RecordingLogger<ModLibrary> _logger = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly SettingsService _settings;

    public ModLibraryTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _repository, new InMemoryDownloadJobRepository([]));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ModRef SkyUi => _site.LinkedFile.Mod;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task WatchesTheModOfALinkWithItsDetailsFromItsSite()
    {
        LibraryMod watched = await Library(_site).WatchAsync(Link, Cancellation);

        Assert.Equal((SkyUi, "SkyUI", true, TestData.Now), (watched.Mod, watched.Name, watched.IsWatched, watched.AddedAt));
        Assert.Empty(watched.Files);
        Assert.Same(watched, Assert.Single(_repository.Mods));
        Assert.Equal(["RecognizeAsync", "GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task WatchingAgainKeepsTheFilesAndUpdatesTheDetails()
    {
        LibraryFile file = File("35407");
        _repository.Add(LibraryMod.Restore(SkyUi, "SkyUI (old name)", "someone", null, isWatched: false, TestData.Now.AddDays(-9), null, [file]));
        _time.Advance(TimeSpan.FromHours(1));

        LibraryMod watched = await Library(_site).WatchAsync(Link, Cancellation);

        Assert.Equal(("SkyUI", (string?)null, true), (watched.Name, watched.Author, watched.IsWatched));
        Assert.Equal(TestData.Now.AddDays(-9), watched.AddedAt);
        Assert.Same(file, Assert.Single(watched.Files));
    }

    [Fact]
    public async Task UnwatchingAModWithoutFilesTakesItOutOfTheLibrary()
    {
        ModLibrary library = Library(_site);
        LibraryMod watched = await library.WatchAsync(Link, Cancellation);

        LibraryChange change = await library.UnwatchAsync(Link, Cancellation);

        Assert.Equal(new LibraryChange(SkyUi, watched, null), change);
        Assert.Empty(await library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task UnwatchingAModWithFilesKeepsThem()
    {
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now).WithFile(File("35407")));

        LibraryChange change = await Library(_site).UnwatchAsync(Link, Cancellation);

        Assert.False(change.After!.IsWatched);
        Assert.Single(change.After.Files);
        Assert.Same(change.After, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task UnwatchesALibraryModByItsPageWithoutAskingItsSite()
    {
        // Such as a mod its site removed, whose links it no longer knows.
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, Link, TestData.Now).WithFile(File("35407")));

        LibraryChange change = await Library(_site).UnwatchAsync(new Uri("http://NexusMods.com/skyrimspecialedition/mods/12604/?tab=files&file_id=35407"), Cancellation);

        Assert.False(change.After!.IsWatched);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task UnwatchingAModNotInTheLibraryChangesNothing()
    {
        LibraryChange change = await Library(_site).UnwatchAsync(Link, Cancellation);

        Assert.Equal(new LibraryChange(SkyUi, null, null), change);
        Assert.DoesNotContain("GetModAsync", _site.Asked);
    }

    [Fact]
    public async Task FindsAndListsWhatTheLibraryHas()
    {
        LibraryMod mod = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now);
        _repository.Add(mod);
        ModLibrary library = Library(_site);

        Assert.Same(mod, await library.FindAsync(SkyUi, Cancellation));
        Assert.Null(await library.FindAsync(new ModRef(SkyUi.Game, "3863"), Cancellation));
        Assert.Equal([mod], await library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task SaysWhenNoSourceKnowsTheLink()
    {
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => Library(ScriptedLinkRecognizer.Never(TestData.Nexus)).WatchAsync(Link, Cancellation));

        Assert.Equal("No source knows this link.", refused.Message);
    }

    [Fact]
    public async Task OnlyWatchesModsOnSitesThatDescribeThem()
    {
        ScriptedLinkRecognizer direct = ScriptedLinkRecognizer.Always(TestData.File(new SourceId("direct"), Link.AbsoluteUri), isCatchAll: true);
        ModLibrary library = Library(direct);

        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(() => library.WatchAsync(Link, Cancellation));
        LibraryChange unwatched = await library.UnwatchAsync(Link, Cancellation);

        Assert.Contains("can watch mods", refused.Message, StringComparison.Ordinal);
        Assert.Null(unwatched.Before);
        Assert.Empty(_repository.Mods);
    }

    public static TheoryData<string, Exception> Failures() => new()
    {
        { "RecognizeAsync", new ArgumentException("That link isn't to a mod.") },
        { "GetModAsync", new AuthenticationFailedException("The site refused the account.") },
        { "GetModAsync", new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound) },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheSiteRefusesAndStoresNothing(string method, Exception failure)
    {
        _site.Failures[method] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => Library(_site).WatchAsync(Link, Cancellation));

        Assert.Same(failure, thrown);
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task WatchesAModByItsReferenceWithItsDetailsFromItsSite()
    {
        LibraryMod watched = await Library(_site).WatchAsync(SkyUi, Cancellation);

        Assert.Equal((SkyUi, "SkyUI", true, TestData.Now), (watched.Mod, watched.Name, watched.IsWatched, watched.AddedAt));
        Assert.Same(watched, Assert.Single(_repository.Mods));
        Assert.Equal(["GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task WatchingAModInTheLibraryByItsReferenceKeepsItsFilesAndUpdatesItsDetails()
    {
        LibraryFile file = File("35407");
        _repository.Add(LibraryMod.Restore(SkyUi, "SkyUI (old name)", "someone", null, isWatched: false, TestData.Now.AddDays(-9), null, [file]));
        ModLibrary library = Library(_site);
        using WatchedChanges seen = new(library);

        LibraryMod watched = await library.WatchAsync(SkyUi, Cancellation);

        Assert.Equal(("SkyUI", (string?)null, true, TestData.Now.AddDays(-9)), (watched.Name, watched.Author, watched.IsWatched, watched.AddedAt));
        Assert.Same(file, Assert.Single(watched.Files));
        Assert.Equal(watched, Assert.Single(await seen.StopAsync()).After);
    }

    [Fact]
    public async Task OnlyWatchesModsByReferenceOnSitesThatDescribeThem()
    {
        ModLibrary library = Library(ScriptedLinkRecognizer.Never(TestData.Nexus));

        await Assert.ThrowsAsync<ArgumentException>(() => library.WatchAsync(SkyUi, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => library.WatchAsync(TestData.File(TestData.ModIo, "1").Mod, Cancellation));
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task WatchingByReferencePassesOnWhatTheSiteRefusesAndStoresNothing()
    {
        HttpRequestException failure = new("No Mod Found", null, HttpStatusCode.NotFound);
        _site.Failures["GetModAsync"] = failure;

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(() => Library(_site).WatchAsync(SkyUi, Cancellation));

        Assert.Same(failure, thrown);
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        Uri relative = new("skyrimspecialedition/mods/12604", UriKind.Relative);
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<ArgumentException>(() => library.WatchAsync(relative, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => library.UnwatchAsync(relative, Cancellation));
        Assert.Empty(_site.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WatchesOrUnwatchesAModInTheLibraryWithoutAskingItsSite(bool watched)
    {
        LibraryMod before = Stored(!watched, File("35407"));

        LibraryChange change = await Library(_site).SetWatchedAsync(SkyUi, watched, Cancellation);

        Assert.Same(before, change.Before);
        Assert.Equal(watched, change.After!.IsWatched);
        Assert.Same(before.Files[0], Assert.Single(change.After.Files));
        Assert.Same(change.After, Assert.Single(_repository.Mods));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task UnwatchingAModWithoutFilesByItsReferenceTakesItOutOfTheLibrary()
    {
        Stored(watched: true);

        LibraryChange change = await Library(_site).SetWatchedAsync(SkyUi, false, Cancellation);

        Assert.Null(change.After);
        Assert.Empty(_repository.Mods);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AskingForWhatAModIsChangesNothing(bool watched)
    {
        LibraryMod before = Stored(watched, File("35407"));
        using WatchedChanges seen = new(_changes);

        LibraryChange change = await Library(_site).SetWatchedAsync(SkyUi, watched, Cancellation);

        Assert.Same(before, change.After);
        Assert.Empty(await seen.StopAsync());
    }

    [Fact]
    public async Task WatchingAModNotInTheLibraryByItsReferenceIsRefused()
    {
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetWatchedAsync(SkyUi, true, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetWatchedAsync(SkyUi, false, Cancellation));
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task DeletesAFileFromDiskAndTheLibrary()
    {
        LibraryFile kept = File("35407", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z");
        LibraryFile deleted = File("35408", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z");
        Stored(watched: false, kept, deleted);

        LibraryChange change = await Library(_site).DeleteFileAsync(deleted.File, Cancellation);

        Assert.Same(kept, Assert.Single(change.After!.Files));
        Assert.Same(change.After, Assert.Single(_repository.Mods));
        Assert.Equal([LibraryPaths.Resolve(Root, kept.RelativePath)], _storage.Files);
        Assert.Empty(_storage.RemovedFolders);
        Assert.Equal(
            ["Information: Deleted SkyUI_5_3_SE.7z, a file of mod 12604 of skyrimspecialedition, from disk and the library "],
            _logger.Entries);
    }

    [Fact]
    public async Task DeletingTheLastFileOfAModNotWatchedTakesItOutAndRemovesTheFoldersLeftEmpty()
    {
        LibraryFile only = File("35407", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z");
        Stored(watched: false, only);
        _storage.Add(Path.Combine(Root, "nexus", "fallout4", "Other (1)", "other.7z"));

        LibraryChange change = await Library(_site).DeleteFileAsync(only.File, Cancellation);

        Assert.Null(change.After);
        Assert.Empty(_repository.Mods);
        Assert.Equal(
            [Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)"), Path.Combine(Root, "nexus", "skyrimspecialedition")],
            _storage.RemovedFolders);
    }

    [Fact]
    public async Task DeletingTheLastFileOfAWatchedModKeepsItWatched()
    {
        Stored(watched: true, File("35407"));

        LibraryChange change = await Library(_site).DeleteFileAsync(File("35407").File, Cancellation);

        Assert.True(change.After!.IsWatched);
        Assert.Empty(change.After.Files);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task DeletesAFileInItsGamesOwnFolderKeepingThatFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _store.Settings = GameIn(folder);
        LibraryFile only = File("35407", "SkyUI (12604)/SkyUI_5_2_SE.7z");
        _repository.Add(LibraryMod.Restore(SkyUi, "SkyUI", null, null, false, TestData.Now, null, [only]));
        _storage.Add(LibraryPaths.Resolve(folder, only.RelativePath));
        _storage.Add(LibraryPaths.Resolve(Root, only.RelativePath));

        await Library(_site).DeleteFileAsync(only.File, Cancellation);

        Assert.Equal([LibraryPaths.Resolve(Root, only.RelativePath)], _storage.Files);
        Assert.Equal([Path.Combine(folder, "SkyUI (12604)")], _storage.RemovedFolders);
    }

    [Fact]
    public async Task AnUnfinishedDownloadIntoItsGamesOwnFolderStopsAForget()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _store.Settings = GameIn(folder);
        Stored(watched: true, File("35407"));
        _downloads.Add(RecordingDownloadService.Job(File("35408").File, Path.Combine(folder, "SkyUI (12604)"), "SkyUI_5_3_SE.7z", DownloadJobStatus.Queued));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Library(_site).ForgetAsync(SkyUi, Cancellation));

        Assert.Equal("A download of a file of SkyUI into the library isn't finished. Let it finish, or cancel it, first.", refused.Message);
    }

    [Fact]
    public async Task TheLibraryRootStaysWhenItsLastFileIsDeleted()
    {
        Stored(watched: false, File("35407"));

        await Library(_site).DeleteFileAsync(File("35407").File, Cancellation);

        Assert.Empty(_storage.RemovedFolders);
    }

    [Fact]
    public async Task AFileThatCantBeDeletedStaysInTheLibrary()
    {
        LibraryMod before = Stored(watched: false, File("35407"));
        string path = LibraryPaths.Resolve(Root, before.Files[0].RelativePath);
        IOException failure = new("The file is open in another program.");
        _storage.Failures[path] = failure;
        using WatchedChanges seen = new(_changes);

        IOException thrown = await Assert.ThrowsAsync<IOException>(() => Library(_site).DeleteFileAsync(before.Files[0].File, Cancellation));

        Assert.Same(failure, thrown);
        Assert.Same(before, Assert.Single(_repository.Mods));
        Assert.Equal([path], _storage.Files);
        Assert.Empty(await seen.StopAsync());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AFileGoneFromDiskOnlyLeavesTheLibrary()
    {
        LibraryMod before = Stored(watched: true, File("35407"));
        _storage.Remove(LibraryPaths.Resolve(Root, before.Files[0].RelativePath));

        LibraryChange change = await Library(_site).DeleteFileAsync(before.Files[0].File, Cancellation);

        Assert.Empty(change.After!.Files);
    }

    [Fact]
    public async Task DeletingAFileTheLibraryDoesntHaveIsRefused()
    {
        Stored(watched: true, File("35407"));
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.DeleteFileAsync(File("99").File, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => library.DeleteFileAsync(new ModFileRef(new ModRef(SkyUi.Game, "3863"), "1"), Cancellation));
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task ForgettingAModTakesItOutWithItsFilesAndUpdatesAndLeavesItsFilesOnDisk()
    {
        LibraryFile older = File("35407", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_1_SE.7z");
        LibraryFile newer = File("35408", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z");
        Stored(watched: true, older, newer);
        LibraryMod stored = (await _repository.FindAsync(SkyUi, Cancellation))!.WithUpdates(
            [new LibraryUpdate(new ModFileRef(SkyUi, "35409"), "SkyUI 5.3", "SkyUI_5_3_SE.7z", "5.3", ModFileCategory.Main, TestData.Now, newer.File, TestData.Now)]);
        await _repository.UpdateAsync(SkyUi, _ => stored, Cancellation);
        string[] onDisk = [.. _storage.Files];

        LibraryChange change = await Library(_site).ForgetAsync(SkyUi, Cancellation);

        Assert.Equal(new LibraryChange(SkyUi, stored, null), change);
        Assert.Empty(_repository.Mods);
        Assert.Equal(onDisk, _storage.Files);
        Assert.Empty(_storage.RemovedFolders);
        Assert.Empty(_downloads.Changes);
        Assert.Empty(_site.Asked);
        Assert.Equal(
            ["Information: Forgot mod 12604 of skyrimspecialedition: its 2 files stay on disk, and 0 of its completed downloads left the queue "],
            _logger.Entries);
    }

    [Fact]
    public async Task ForgetsALibraryModByItsPageWithoutAskingItsSite()
    {
        // Such as a mod its site removed, whose links it no longer knows.
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, Link, TestData.Now).WithFile(File("35407")));

        LibraryChange change = await Library(_site).ForgetAsync(new Uri("http://NexusMods.com/skyrimspecialedition/mods/12604/?tab=files&file_id=35407"), Cancellation);

        Assert.Null(change.After);
        Assert.Empty(_repository.Mods);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task ForgetsTheModALinkIsToAsItsSiteSays()
    {
        Stored(watched: false, File("35407"));

        LibraryChange change = await Library(_site).ForgetAsync(Link, Cancellation);

        Assert.Equal((SkyUi, (LibraryMod?)null), (change.Mod, change.After));
        Assert.Empty(_repository.Mods);
        Assert.Equal(["RecognizeAsync"], _site.Asked);
    }

    [Fact]
    public async Task AModNotInTheLibraryIsNamedWithItsSitesName()
    {
        ModLibrary library = Library(new FakeModSite());

        KeyNotFoundException refused = await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetPinnedAsync(new ModRef(FakeModSite.Game, "7"), true, Cancellation));

        Assert.Equal("Mod 7 of skyrimspecialedition on Fake mod site isn't in the library.", refused.Message);
    }

    [Fact]
    public async Task AModOfASiteNoLongerHereIsNamedWithTheSitesId()
    {
        ModLibrary library = Library();

        KeyNotFoundException refused = await Assert.ThrowsAsync<KeyNotFoundException>(() => library.ForgetAsync(SkyUi, Cancellation));

        Assert.Equal("Mod 12604 of skyrimspecialedition on nexus isn't in the library.", refused.Message);
    }

    [Fact]
    public async Task ForgettingAModNotInTheLibraryIsRefused()
    {
        ModLibrary library = Library(_site);
        _repository.Add(LibraryMod.Watch(new ModRef(SkyUi.Game, "3863"), "SkyUI Survival", null, null, TestData.Now));
        _downloads.Add(RecordingDownloadService.Job(
            File("35407").File, Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)"), "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed));

        KeyNotFoundException byRef = await Assert.ThrowsAsync<KeyNotFoundException>(() => library.ForgetAsync(SkyUi, Cancellation));
        KeyNotFoundException byLink = await Assert.ThrowsAsync<KeyNotFoundException>(() => library.ForgetAsync(Link, Cancellation));

        Assert.Equal("Mod 12604 of skyrimspecialedition on nexus isn't in the library.", byRef.Message);
        Assert.Equal(byRef.Message, byLink.Message);
        Assert.Single(_repository.Mods);
        Assert.Empty(_downloads.Changes);
    }

    [Fact]
    public async Task ForgettingAModClearsItsCompletedDownloadsIntoTheLibraryFromTheQueue()
    {
        // Left there, the next start would record them again, and the mod would be back.
        Stored(watched: true, File("35407"));
        string folder = Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        DownloadJobSnapshot completed = RecordingDownloadService.Job(File("35407").File, folder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed);
        DownloadJobSnapshot again = RecordingDownloadService.Job(File("35407").File, folder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed);
        DownloadJobSnapshot cancelled = RecordingDownloadService.Job(File("35408").File, folder, "SkyUI_5_3_SE.7z", DownloadJobStatus.Cancelled);
        DownloadJobSnapshot elsewhere = RecordingDownloadService.Job(File("35407").File, TestData.TargetDirectory, "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed);
        DownloadJobSnapshot otherMod = RecordingDownloadService.Job(
            new ModFileRef(new ModRef(SkyUi.Game, "3863"), "1"), Path.Combine(Root, "nexus", "skyrimspecialedition", "Survival (3863)"), "survival.7z", DownloadJobStatus.Completed);
        foreach (DownloadJobSnapshot job in new[] { completed, again, cancelled, elsewhere, otherMod })
        {
            _downloads.Add(job);
        }

        await Library(_site).ForgetAsync(SkyUi, Cancellation);

        Assert.Equivalent(new[] { completed.Id, again.Id }, Assert.Single(_downloads.ClearedCompleted), strict: true);
        Assert.Equal([cancelled.Id, elsewhere.Id, otherMod.Id], (await _downloads.ListAsync(Cancellation)).Select(job => job.Id));
        Assert.Empty(_repository.Mods);
        Assert.Equal(
            ["Information: Forgot mod 12604 of skyrimspecialedition: its 1 files stay on disk, and 2 of its completed downloads left the queue "],
            _logger.Entries);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Queued)]
    [InlineData(DownloadJobStatus.Resolving)]
    [InlineData(DownloadJobStatus.Downloading)]
    [InlineData(DownloadJobStatus.Verifying)]
    [InlineData(DownloadJobStatus.Paused)]
    [InlineData(DownloadJobStatus.Failed)]
    [InlineData(DownloadJobStatus.AwaitingHandoff)]
    public async Task AnUnfinishedDownloadOfItsFilesIntoTheLibraryStopsAForget(DownloadJobStatus status)
    {
        // Once it finished, it would be recorded, and the mod would be back.
        LibraryMod stored = Stored(watched: true, File("35407"));
        string folder = Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        _downloads.Add(RecordingDownloadService.Job(File("35407").File, folder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed));
        _downloads.Add(RecordingDownloadService.Job(File("35408").File, folder, "SkyUI_5_3_SE.7z", status));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Library(_site).ForgetAsync(SkyUi, Cancellation));

        Assert.Equal("A download of a file of SkyUI into the library isn't finished. Let it finish, or cancel it, first.", refused.Message);
        Assert.Same(stored, Assert.Single(_repository.Mods));
        Assert.Equal(2, (await _downloads.ListAsync(Cancellation)).Count);
        Assert.Empty(_downloads.Changes);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task UnfinishedDownloadsOfItsFilesIntoTheLibraryAreCountedAndOnesElsewhereDontCount()
    {
        Stored(watched: true, File("35407"));
        string folder = Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        _downloads.Add(RecordingDownloadService.Job(File("35407").File, folder, "SkyUI_5_2_SE.7z", DownloadJobStatus.Queued));
        _downloads.Add(RecordingDownloadService.Job(File("35408").File, folder, "SkyUI_5_3_SE.7z", DownloadJobStatus.Downloading));
        ModLibrary library = Library(_site);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => library.ForgetAsync(SkyUi, Cancellation));
        _downloads.Update(RecordingDownloadService.Job(File("35407").File, TestData.TargetDirectory, "SkyUI_5_2_SE.7z", DownloadJobStatus.Downloading));
        foreach (DownloadJobSnapshot job in (await _downloads.ListAsync(Cancellation)).Where(job => job.TargetDirectory == folder))
        {
            _downloads.Update(job with { Status = DownloadJobStatus.Cancelled });
        }

        LibraryChange change = await library.ForgetAsync(SkyUi, Cancellation);

        Assert.Equal("2 downloads of files of SkyUI into the library aren't finished. Let them finish, or cancel them, first.", refused.Message);
        Assert.Null(change.After);
        Assert.Empty(_downloads.ClearedCompleted);
    }

    [Fact]
    public async Task AQueueThatCantBeClearedLeavesTheModInTheLibrary()
    {
        LibraryMod stored = Stored(watched: true, File("35407"));
        _downloads.Add(RecordingDownloadService.Job(
            File("35407").File, Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)"), "SkyUI_5_2_SE.7z", DownloadJobStatus.Completed));
        _downloads.ClearRefusal = new InvalidOperationException("The download scheduler has stopped.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Library(_site).ForgetAsync(SkyUi, Cancellation));

        Assert.Same(stored, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task EveryChangeIsToldToWhoWatchesTheLibrary()
    {
        ModLibrary library = Library(_site);
        using WatchedChanges seen = new(library);

        LibraryMod watched = await library.WatchAsync(Link, Cancellation);
        await library.UnwatchAsync(Link, Cancellation);
        await library.UnwatchAsync(Link, Cancellation);
        Stored(watched: false, File("35407"));
        LibraryChange rewatched = await library.SetWatchedAsync(SkyUi, true, Cancellation);
        LibraryChange deleted = await library.DeleteFileAsync(File("35407").File, Cancellation);
        Stored(watched: false, File("35407"));
        LibraryChange forgotten = await library.ForgetAsync(SkyUi, Cancellation);

        Assert.Equal(
            [new LibraryChange(SkyUi, null, watched), new LibraryChange(SkyUi, watched, null), rewatched, deleted, forgotten],
            await seen.StopAsync());
    }

    [Fact]
    public async Task WatchingTheLibraryStartsWhenAsked()
    {
        ModLibrary library = Library(_site);
        using CancellationTokenSource stop = new();
        IAsyncEnumerable<LibraryChange> changes = library.ChangesAsync(stop.Token);

        LibraryMod watched = await library.WatchAsync(Link, Cancellation);
        await stop.CancelAsync();

        Assert.Equal([new LibraryChange(SkyUi, null, watched)], await changes.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task DeletingAFileKeepsItInTheHistory()
    {
        LibraryFile kept = File("35407", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z");
        LibraryFile deleted = File("35408", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z");
        Stored(watched: false, kept, deleted);
        _time.Advance(TimeSpan.FromHours(1));

        await Library(_site).DeleteFileAsync(deleted.File, Cancellation);

        LibraryEvent happened = Assert.Single(_history.Events);
        Assert.Equal(
            new LibraryEvent(
                TestData.Now.AddHours(1),
                LibraryEventKind.Deleted,
                SkyUi,
                "SkyUI",
                deleted.File,
                "SkyUI 5.2",
                "5.2",
                "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z"),
            happened);
    }

    [Fact]
    public async Task DeletingTheLastFileOfAModNotWatchedKeepsItInTheHistory()
    {
        Stored(watched: false, File("35407"));

        await Library(_site).DeleteFileAsync(File("35407").File, Cancellation);

        Assert.Equal((LibraryEventKind.Deleted, "SkyUI"), (Assert.Single(_history.Events).Kind, _history.Events[0].ModName));
    }

    [Fact]
    public async Task AFileThatCantBeDeletedIsntInTheHistory()
    {
        LibraryMod before = Stored(watched: false, File("35407"));
        _storage.Failures[LibraryPaths.Resolve(Root, before.Files[0].RelativePath)] = new IOException("The file is open in another program.");

        await Assert.ThrowsAsync<IOException>(() => Library(_site).DeleteFileAsync(before.Files[0].File, Cancellation));

        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task AHistoryThatCantKeepADeletionLeavesTheFileDeleted()
    {
        Stored(watched: true, File("35407"));
        _history.Failure = new InvalidDataException("The database is locked.");

        LibraryChange change = await Library(_site).DeleteFileAsync(File("35407").File, Cancellation);

        Assert.Empty(change.After!.Files);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task ForgettingAModKeepsItInTheHistory()
    {
        Stored(watched: true, File("35407"));

        await Library(_site).ForgetAsync(SkyUi, Cancellation);

        Assert.Equal(new LibraryEvent(TestData.Now, LibraryEventKind.Removed, SkyUi, "SkyUI"), Assert.Single(_history.Events));
    }

    [Fact]
    public async Task ForgettingAModNotInTheLibraryKeepsNothing()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Library(_site).ForgetAsync(SkyUi, Cancellation));

        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task WatchingAndUnwatchingArentInTheHistory()
    {
        ModLibrary library = Library(_site);

        await library.WatchAsync(Link, Cancellation);
        await library.SetWatchedAsync(SkyUi, false, Cancellation);

        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task PinsAModWithoutAskingItsSite()
    {
        Stored(watched: true, File("35407"));
        using WatchedChanges seen = new(_changes);

        LibraryChange change = await Library(_site).SetPinnedAsync(SkyUi, true, Cancellation);

        Assert.True(change.After!.IsPinned);
        Assert.False(change.Before!.IsPinned);
        Assert.Same(change.After, Assert.Single(_repository.Mods));
        Assert.Equal([change], await seen.StopAsync());
        Assert.Empty(_site.Asked);

        LibraryChange unpinned = await Library(_site).SetPinnedAsync(SkyUi, false, Cancellation);

        Assert.False(unpinned.After!.IsPinned);
    }

    [Fact]
    public async Task PinsAndKeepsANoteOfAModByItsPageWithoutAskingItsSite()
    {
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, Link, TestData.Now).WithFile(File("35407")));
        Uri onItsPage = new("http://NexusMods.com/skyrimspecialedition/mods/12604/?tab=files&file_id=35407");

        await Library(_site).SetPinnedAsync(onItsPage, true, Cancellation);
        await Library(_site).SetNoteAsync(onItsPage, "Stay on 5.2.", Cancellation);

        LibraryMod mod = Assert.Single(_repository.Mods);
        Assert.Equal((true, "Stay on 5.2."), (mod.IsPinned, mod.Note));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task PinningSkippingOrNotingAModNotInTheLibraryIsRefused()
    {
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetPinnedAsync(SkyUi, true, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetPinnedAsync(Link, true, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SkipUpdateAsync(File("35408").File, true, Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetNoteAsync(SkyUi, "A note.", Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => library.SetNoteAsync(Link, "A note.", Cancellation));
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task SkipsAnUpdateByItsOwnLinkWithoutAskingItsSite()
    {
        Stored(watched: true, File("35407"));
        Uri page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35408");
        await _repository.UpdateAsync(SkyUi, stored => stored!.WithUpdates([Update("35408", page)]), Cancellation);
        using WatchedChanges seen = new(_changes);

        LibraryChange change = await Library(_site).SkipUpdateAsync(
            new Uri("http://nexusmods.com/skyrimspecialedition/mods/12604/?tab=files&file_id=35408"), true, Cancellation);

        Assert.Equal([new ModFileRef(SkyUi, "35408")], change.After!.SkippedUpdates);
        Assert.Empty(change.After.OfferedUpdates);
        Assert.Single(await seen.StopAsync());
        Assert.Empty(_site.Asked);

        LibraryChange offered = await Library(_site).SkipUpdateAsync(new ModFileRef(SkyUi, "35408"), false, Cancellation);

        Assert.Empty(offered.After!.SkippedUpdates);
    }

    [Fact]
    public async Task SkipsTheUpdateWhoseLinkNamesItsFile()
    {
        Stored(watched: true, File("35407"));
        await _repository.UpdateAsync(
            SkyUi,
            stored => stored!.WithUpdates(
            [
                Update("35408", new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35408")),
                Update("35409", new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35409")),
            ]),
            Cancellation);

        LibraryChange change = await Library(_site).SkipUpdateAsync(
            new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35409"), true, Cancellation);

        Assert.Equal([new ModFileRef(SkyUi, "35409")], change.After!.SkippedUpdates);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task SkipsAnUpdateOfAnotherFilesLinkAsItsSiteSays()
    {
        // The site says the link is to file 35407, which the library doesn't have: a new main file of a mod only watched.
        _repository.Add(LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now));
        await _repository.UpdateAsync(SkyUi, stored => stored!.WithUpdates([Update("35407", null)]), Cancellation);

        LibraryChange change = await Library(_site).SkipUpdateAsync(Link, true, Cancellation);

        Assert.Equal([new ModFileRef(SkyUi, "35407")], change.After!.SkippedUpdates);
        Assert.Equal(["RecognizeAsync"], _site.Asked);
    }

    [Fact]
    public async Task SkippingAFileThatIsntAnUpdateIsRefused()
    {
        LibraryMod before = Stored(watched: true, File("35407"));
        ModLibrary library = Library(_site);

        await Assert.ThrowsAsync<ArgumentException>(() => library.SkipUpdateAsync(File("35407").File, true, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => library.SkipUpdateAsync(Link, true, Cancellation));
        Assert.Same(before, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task KeepsANoteAndTakesItAway()
    {
        Stored(watched: false, File("35407"));
        ModLibrary library = Library(_site);

        LibraryChange noted = await library.SetNoteAsync(SkyUi, "  Stay on 5.2: 5.3 breaks the map.  ", Cancellation);
        LibraryChange removed = await library.SetNoteAsync(SkyUi, " ", Cancellation);

        Assert.Equal("Stay on 5.2: 5.3 breaks the map.", noted.After!.Note);
        Assert.Null(removed.After!.Note);
        Assert.Null(Assert.Single(_repository.Mods).Note);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task ANoteTooLongIsRefused()
    {
        LibraryMod before = Stored(watched: true, File("35407"));

        await Assert.ThrowsAsync<ArgumentException>(() => Library(_site).SetNoteAsync(SkyUi, new string('a', 2001), Cancellation));

        Assert.Same(before, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task UndoingAForgetPutsTheModBackWithItsFilesUpdatesPinSkipAndNote()
    {
        LibraryMod kept = Kept();
        string[] onDisk = [.. _storage.Files];
        ModLibrary library = Library(_site);
        LibraryChange forgotten = await library.ForgetAsync(SkyUi, Cancellation);
        using WatchedChanges seen = new(_changes);

        LibraryChange restored = await library.RestoreAsync(forgotten, Cancellation);

        Assert.Null(restored.Before);
        Assert.Same(restored.After, Assert.Single(_repository.Mods));
        Assert.Equal(Facts(kept), Facts(restored.After!));
        Assert.Equal([restored], await seen.StopAsync());
        Assert.Equal(onDisk, _storage.Files);
        Assert.Empty(_site.Asked);
        Assert.Equal("Information: Undid a change to mod 12604 of skyrimspecialedition ", _logger.Entries.Last());
    }

    [Fact]
    public async Task UndoingAnUnwatchWatchesItAgainWithItsUpdates()
    {
        LibraryMod kept = Kept();
        ModLibrary library = Library(_site);
        LibraryChange unwatched = await library.SetWatchedAsync(SkyUi, false, Cancellation);
        Assert.Empty(unwatched.After!.Updates);

        LibraryChange restored = await library.RestoreAsync(unwatched, Cancellation);

        Assert.Equal(Facts(kept), Facts(restored.After!));
    }

    [Fact]
    public async Task UndoesAPinAnUnpinASkipAndANoteRemoved()
    {
        LibraryMod kept = Kept();
        ModLibrary library = Library(_site);

        LibraryChange[] changes =
        [
            await library.SetPinnedAsync(SkyUi, false, Cancellation),
            await library.SkipUpdateAsync(new ModFileRef(SkyUi, "35409"), false, Cancellation),
            await library.SetNoteAsync(SkyUi, null, Cancellation),
        ];
        Assert.Equal((false, 0, (string?)null), (changes[^1].After!.IsPinned, changes[^1].After!.SkippedUpdates.Count, changes[^1].After!.Note));
        foreach (LibraryChange change in changes.Reverse())
        {
            await library.RestoreAsync(change, Cancellation);
        }

        Assert.Equal(Facts(kept), Facts(Assert.Single(_repository.Mods)));

        LibraryChange pinned = await library.SetPinnedAsync(SkyUi, false, Cancellation);
        LibraryChange unpinned = await library.RestoreAsync(pinned, Cancellation);
        Assert.True(unpinned.After!.IsPinned);
    }

    [Fact]
    public async Task UndoingKeepsTheFilesAsTheLibraryHasThemNow()
    {
        Kept();
        ModLibrary library = Library(_site);
        LibraryChange unpinned = await library.SetPinnedAsync(SkyUi, false, Cancellation);
        LibraryFile recorded = File("35410", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_4_SE.7z");
        await _repository.UpdateAsync(SkyUi, mod => mod!.WithFile(recorded).WithoutFile(new ModFileRef(SkyUi, "35407")), Cancellation);

        LibraryChange restored = await library.RestoreAsync(unpinned, Cancellation);

        Assert.True(restored.After!.IsPinned);
        Assert.Equal(["35408", "35410"], restored.After.Files.Select(file => file.File.Id).Order());
        Assert.Equal(["35409"], restored.After.Updates.Select(update => update.File.Id));
    }

    [Fact]
    public async Task AModForgottenAndBackSinceStaysAsItIs()
    {
        Kept();
        ModLibrary library = Library(_site);
        LibraryChange forgotten = await library.ForgetAsync(SkyUi, Cancellation);
        LibraryMod back = await library.WatchAsync(SkyUi, Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => library.RestoreAsync(forgotten, Cancellation));

        Assert.Equal("SkyUI is in the library again since, so it stays as it is now.", refused.Message);
        Assert.Same(back, Assert.Single(_repository.Mods));
    }

    [Fact]
    public async Task AModGoneSinceStaysOut()
    {
        Kept();
        ModLibrary library = Library(_site);
        LibraryChange unpinned = await library.SetPinnedAsync(SkyUi, false, Cancellation);
        await library.ForgetAsync(SkyUi, Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => library.RestoreAsync(unpinned, Cancellation));

        Assert.Equal("SkyUI left the library since, so it stays out.", refused.Message);
        Assert.Empty(_repository.Mods);
    }

    [Fact]
    public async Task AChangeThatAddedTheModIsntUndone()
    {
        ModLibrary library = Library(_site);
        LibraryMod watched = await library.WatchAsync(Link, Cancellation);

        await Assert.ThrowsAsync<ArgumentException>(() => library.RestoreAsync(new LibraryChange(SkyUi, null, watched), Cancellation));

        Assert.Same(watched, Assert.Single(_repository.Mods));
    }

    /// <summary>
    /// SkyUI watched, with two files on disk, an update to the newer one skipped, pinned, and with a note.
    /// </summary>
    private LibraryMod Kept()
    {
        LibraryFile older = File("35407", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_1_SE.7z");
        LibraryFile newer = File("35408", "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z");
        LibraryMod stored = LibraryMod.Restore(SkyUi, "SkyUI", null, null, isWatched: true, TestData.Now, null, [older, newer])
            .WithUpdates([new LibraryUpdate(new ModFileRef(SkyUi, "35409"), "SkyUI 5.3", "SkyUI_5_3_SE.7z", "5.3", ModFileCategory.Main, TestData.Now, newer.File, TestData.Now)])
            .WithUpdateSkipped(new ModFileRef(SkyUi, "35409"), true)
            .WithPinned(true)
            .WithNote("Keep 5.2 for now.");
        _repository.Add(stored);
        _storage.Add(LibraryPaths.Resolve(Root, older.RelativePath));
        _storage.Add(LibraryPaths.Resolve(Root, newer.RelativePath));
        return stored;
    }

    /// <summary>
    /// What undoing puts back, as text: watched or not, the files, the updates, pinned or not, those skipped, the note.
    /// </summary>
    private static string Facts(LibraryMod mod)
        => string.Join(
            " | ",
            mod.IsWatched,
            string.Join(',', mod.Files.Select(file => file.File.Id).Order(StringComparer.Ordinal)),
            string.Join(',', mod.Updates.Select(update => update.File.Id)),
            mod.IsPinned,
            string.Join(',', mod.SkippedUpdates.Select(file => file.Id)),
            mod.Note);

    private LibraryUpdate Update(string id, Uri? page)
        => new(new ModFileRef(SkyUi, id), $"SkyUI {id}", $"SkyUI_{id}.7z", "5.3", ModFileCategory.Main, TestData.Now, page is null ? null : new ModFileRef(SkyUi, "35407"), TestData.Now, page);

    private static LibraryFile File(string id, string relativePath = "SkyUI_5_2_SE.7z")
        => new(new ModFileRef(TestData.File(TestData.Nexus, id).Mod, id), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now, relativePath, 1, TestData.Now);

    private static AppSettings GameIn(string folder)
        => new()
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer)
            {
                [TestData.File(TestData.Nexus, "1").Mod.Game] = new() { Root = folder },
            },
        };

    private ModLibrary Library(params IModSource[] sources)
        => new(new SourceRegistry(sources), _repository, _settings, _storage, _downloads, _downloads, _changes, _history.History(_time), _time, _logger);

    /// <summary>
    /// SkyUI in the library, watched or not, with its files on disk.
    /// </summary>
    private LibraryMod Stored(bool watched, params LibraryFile[] files)
    {
        LibraryMod mod = LibraryMod.Restore(SkyUi, "SkyUI", null, null, watched, TestData.Now, null, files);
        _repository.Add(mod);
        foreach (LibraryFile file in files)
        {
            _storage.Add(LibraryPaths.Resolve(Root, file.RelativePath));
        }

        return mod;
    }
}
