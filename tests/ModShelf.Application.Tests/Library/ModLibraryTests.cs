using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
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
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly FakeCatalogSource _site = new(TestData.Nexus);
    private readonly FakeLibraryStorage _storage = new();
    private readonly LibraryChanges _changes = new();
    private readonly RecordingLogger<ModLibrary> _logger = new();
    private readonly SettingsService _settings;

    public ModLibraryTests()
    {
        _settings = new SettingsService(new InMemorySettingsStore(), new FakeUserFolders(), _repository, new InMemoryDownloadJobRepository([]));
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

        Assert.Equal(
            [new LibraryChange(SkyUi, null, watched), new LibraryChange(SkyUi, watched, null), rewatched, deleted],
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

    private static LibraryFile File(string id, string relativePath = "SkyUI_5_2_SE.7z")
        => new(new ModFileRef(TestData.File(TestData.Nexus, id).Mod, id), "SkyUI 5.2", "5.2", ModFileCategory.Main, TestData.Now, relativePath, 1, TestData.Now);

    private ModLibrary Library(params IModSource[] sources)
        => new(new SourceRegistry(sources), _repository, _settings, _storage, _changes, _time, _logger);

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
