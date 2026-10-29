using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// What recording a download does with the files it replaced, by the rule in the settings. SkyUI 5.0 was replaced by
/// 5.1, 5.1 by 5.2, and 5.2 by 5.3; the library has 5.0 to 5.2, on disk, and 5.3 has just been downloaded.
/// </summary>
public sealed class LibraryOldVersionsTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string Folder = "fake-mods/skyrimspecialedition/SkyUI (12604)";

    private static readonly string[] OldIds = ["50", "51", "52"];

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly FakeModSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<LibraryDownloads> _logger = new();
    private readonly SettingsService _settings;

    public LibraryOldVersionsTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, "50", "SkyUI 5.0.7z", replacedBy: "51");
        _site.AddFile(SkyUi, "51", "SkyUI 5.1.7z", replacedBy: "52");
        _site.AddFile(SkyUi, "52", "SkyUI 5.2.7z", replacedBy: "53");
        _site.AddFile(SkyUi, "53", "SkyUI 5.3.7z");
        LibraryMod mod = LibraryMod.Watch(Mod(SkyUi), "SkyUI", null, null, TestData.Now);
        foreach (string id in OldIds)
        {
            mod = mod.WithFile(Stored(id));
            _storage.Add(InRoot(Stored(id).RelativePath));
        }

        _library.Add(mod);
        _storage.Add(InRoot($"{Folder}/SkyUI 5.3.7z"));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    private IEnumerable<string> LibraryPaths => Assert.Single(_library.Mods).Files.Select(file => file.RelativePath);

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task ByDefaultKeepsTheNewestTwoAndDeletesTheRest()
    {
        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["51", "50"], recorded.Deleted.Select(file => file.File.Id));
        Assert.Empty(recorded.Archived);
        Assert.Equal([$"{Folder}/SkyUI 5.2.7z", $"{Folder}/SkyUI 5.3.7z"], LibraryPaths);
        Assert.False(_storage.FileExists(InRoot($"{Folder}/SkyUI 5.0.7z")));
        Assert.False(_storage.FileExists(InRoot($"{Folder}/SkyUI 5.1.7z")));
        Assert.True(_storage.FileExists(InRoot($"{Folder}/SkyUI 5.2.7z")));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Deleted the old version SkyUI 5.0.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsAsManyVersionsAsTheRuleSays()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.KeepLatest(1) };

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["52", "51", "50"], recorded.Deleted.Select(file => file.File.Id));
        Assert.Equal([$"{Folder}/SkyUI 5.3.7z"], LibraryPaths);
    }

    [Fact]
    public async Task KeepingAllChangesNothing()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.KeepAll };

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal((0, 0), (recorded.Archived.Count, recorded.Deleted.Count));
        Assert.Equal(4, LibraryPaths.Count());
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task ArchivesTheOldVersionsInTheirFolder()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };

        LibraryRecord recorded = await RecordAsync();

        string[] archived = [$"{Folder}/_archive/SkyUI 5.2.7z", $"{Folder}/_archive/SkyUI 5.1.7z", $"{Folder}/_archive/SkyUI 5.0.7z"];
        Assert.Equal(archived, recorded.Archived.Select(file => file.RelativePath));
        Assert.Equal([archived[2], archived[1], archived[0], $"{Folder}/SkyUI 5.3.7z"], LibraryPaths);
        Assert.All(archived, path => Assert.True(_storage.FileExists(InRoot(path))));
        Assert.False(_storage.FileExists(InRoot($"{Folder}/SkyUI 5.0.7z")));
        Assert.Equal(("SkyUI 50", 100L), (recorded.Archived[2].Name, recorded.Archived[2].SizeInBytes));
    }

    [Fact]
    public async Task AddsTheFileIdToANameTakenInTheArchive()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.Add(InRoot($"{Folder}/_archive/SkyUI 5.2.7z"));
        _storage.Add(InRoot($"{Folder}/_archive/SkyUI 5.1.7z"));
        _storage.Add(InRoot($"{Folder}/_archive/SkyUI 5.1 (51).7z"));

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal([$"{Folder}/_archive/SkyUI 5.2 (52).7z", $"{Folder}/_archive/SkyUI 5.0.7z"], recorded.Archived.Select(file => file.RelativePath));
        Assert.Contains($"{Folder}/SkyUI 5.1.7z", LibraryPaths);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Didn't archive the old version SkyUI 5.1.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANameAnotherLibraryFileHasInTheArchiveIsTaken()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _library.Add(LibraryMod.Watch(Mod("3863"), "SKSE", null, null, TestData.Now).WithFile(
            new LibraryFile(File("3863", "1"), "SKSE", null, ModFileCategory.Main, TestData.Now, $"{Folder}/_archive/SkyUI 5.2.7z", 1, TestData.Now)));

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal($"{Folder}/_archive/SkyUI 5.2 (52).7z", recorded.Archived[0].RelativePath);
    }

    [Fact]
    public async Task AnOldVersionThatCantMoveStaysWhereItIs()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.Failures[InRoot($"{Folder}/SkyUI 5.1.7z")] = new IOException("In use.");

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["52", "50"], recorded.Archived.Select(file => file.File.Id));
        Assert.Contains($"{Folder}/SkyUI 5.1.7z", LibraryPaths);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't move the old version SkyUI 5.1.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOldVersionWhosePathInTheArchiveWouldBeTooLongStaysWhereItIs()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.MaxPath = InRoot($"{Folder}/_archive/SkyUI 5.2.7z").Length - 1;

        LibraryRecord recorded = await RecordAsync();

        Assert.Empty(recorded.Archived);
        Assert.Contains($"{Folder}/SkyUI 5.2.7z", LibraryPaths);
        Assert.Empty(_storage.Moves);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Didn't archive the old version SkyUI 5.2.7z: its path in the _archive folder would be too long", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOldVersionGoneFromDiskIsntArchived()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.Remove(InRoot($"{Folder}/SkyUI 5.0.7z"));

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["52", "51"], recorded.Archived.Select(file => file.File.Id));
        Assert.Contains($"{Folder}/SkyUI 5.0.7z", LibraryPaths);
        Assert.DoesNotContain(_logger.Entries, entry => entry.StartsWith("Warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOldVersionInTheArchiveAlreadyAtItsSizeIsListedThere()
    {
        // A record that stopped after archiving 5.0, such as when the app stopped, before the library changed.
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.Remove(InRoot($"{Folder}/SkyUI 5.0.7z"));
        _storage.Add(InRoot($"{Folder}/_archive/SkyUI 5.0.7z"));

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["52", "51", "50"], recorded.Archived.Select(file => file.File.Id));
        Assert.Contains($"{Folder}/_archive/SkyUI 5.0.7z", LibraryPaths);
        Assert.DoesNotContain(_storage.Moves, move => move.Destination == InRoot($"{Folder}/_archive/SkyUI 5.0.7z"));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Found the old version SkyUI 5.0.7z in its _archive folder already", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherFileInTheArchiveUnderAnOldVersionsNameIsntIt()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _storage.Remove(InRoot($"{Folder}/SkyUI 5.0.7z"));
        _storage.Add(InRoot($"{Folder}/_archive/SkyUI 5.0.7z"), 99);

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["52", "51"], recorded.Archived.Select(file => file.File.Id));
        Assert.Contains($"{Folder}/SkyUI 5.0.7z", LibraryPaths);
    }

    [Fact]
    public async Task AnOldVersionThatCantBeDeletedStaysInTheLibrary()
    {
        _storage.Failures[InRoot($"{Folder}/SkyUI 5.1.7z")] = new UnauthorizedAccessException("Read-only.");

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["50"], recorded.Deleted.Select(file => file.File.Id));
        Assert.Contains($"{Folder}/SkyUI 5.1.7z", LibraryPaths);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't delete the old version SkyUI 5.1.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOldVersionGoneFromDiskLeavesTheLibraryToo()
    {
        _storage.Remove(InRoot($"{Folder}/SkyUI 5.0.7z"));

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["51", "50"], recorded.Deleted.Select(file => file.File.Id));
    }

    [Fact]
    public async Task ArchivedFilesGoBackWhenTheLibraryRefusesTheChange()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        _library.UpdateFailure = new InvalidDataException("The database is locked.");

        await Assert.ThrowsAsync<InvalidDataException>(RecordAsync);

        Assert.All(OldIds, id => Assert.True(_storage.FileExists(InRoot(Stored(id).RelativePath))));
        Assert.DoesNotContain(_storage.Files, path => path.Contains("_archive", StringComparison.Ordinal));
        Assert.Equal(3, LibraryPaths.Count());
    }

    [Fact]
    public async Task OldVersionsStayOnDiskWhenTheLibraryRefusesTheChange()
    {
        _library.UpdateFailure = new InvalidDataException("The database is locked.");

        await Assert.ThrowsAsync<InvalidDataException>(RecordAsync);

        Assert.All(OldIds, id => Assert.True(_storage.FileExists(InRoot(Stored(id).RelativePath))));
        Assert.Equal(3, LibraryPaths.Count());
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("Deleted the old version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldVersionsLeaveTheDiskOnlyOnceTheLibraryHasChanged()
    {
        List<bool> listedWhenDeleted = [];
        _storage.BeforeDelete = path => listedWhenDeleted.Add(LibraryPaths.Any(listed => InRoot(listed) == path));

        await RecordAsync();

        Assert.Equal([false, false], listedWhenDeleted);
    }

    [Fact]
    public async Task AnOldVersionThatCantBeDeletedIsListedAgain()
    {
        _storage.Failures[InRoot($"{Folder}/SkyUI 5.1.7z")] = new IOException("In use.");
        LibraryChanges published = new();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        IAsyncEnumerable<LibraryChange> watched = published.Watch(stop.Token);

        using (LibraryDownloads library = Create(published))
        {
            await library.RecordAsync(Done("53", "SkyUI 5.3.7z"), Cancellation);
        }

        await stop.CancelAsync();
        List<LibraryChange> changes = await watched.ToListAsync(Cancellation);
        Assert.Equal(2, changes.Count);
        Assert.DoesNotContain(changes[0].After!.Files, file => file.File.Id == "51");
        Assert.Contains(changes[1].After!.Files, file => file.File.Id == "51");
        Assert.Equal(LibraryPaths, changes[1].After!.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task AnOldVersionThatCantBeDeletedNorListedAgainStaysOnDisk()
    {
        _storage.Failures[InRoot($"{Folder}/SkyUI 5.1.7z")] = new IOException("In use.");
        _library.AfterUpdate = _ => _library.UpdateFailure = new InvalidDataException("The database is locked.");

        LibraryRecord recorded = await RecordAsync();

        Assert.Equal(["50"], recorded.Deleted.Select(file => file.File.Id));
        Assert.True(_storage.FileExists(InRoot($"{Folder}/SkyUI 5.1.7z")));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: Couldn't list the old version SkyUI 5.1.7z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordingTheSameDownloadAgainSaysWhatWasDone()
    {
        _store.Settings = new AppSettings { OldVersions = OldVersionRule.Archive };
        DownloadJobSnapshot done = Done("53", "SkyUI 5.3.7z");
        using LibraryDownloads library = Create();
        LibraryRecord first = (await library.RecordAsync(done, Cancellation))!;

        LibraryRecord again = (await library.RecordAsync(done, Cancellation))!;

        Assert.Equal(3, again.Archived.Count);
        Assert.Equal(first.Archived, again.Archived);
        Assert.Equal(3, _storage.Moves.Count);
    }

    [Fact]
    public async Task ADownloadOfAnOlderVersionLeavesTheNewerOnes()
    {
        _site.AddFile(SkyUi, "49", "SkyUI 4.9.7z", replacedBy: "50");
        _storage.Add(InRoot($"{Folder}/SkyUI 4.9.7z"));
        using LibraryDownloads library = Create();

        LibraryRecord? recorded = await library.RecordAsync(Done("49", "SkyUI 4.9.7z"), Cancellation);

        Assert.Empty(recorded!.Deleted);
        Assert.Equal(4, LibraryPaths.Count());
    }

    [Fact]
    public async Task ADownloadAgainOfAFileTheLibraryHasChangesNoOtherFile()
    {
        _time.Advance(TimeSpan.FromDays(1));
        DownloadJobSnapshot again = Done("52", "SkyUI 5.2.7z") with { CreatedAt = _time.GetUtcNow() };
        using LibraryDownloads library = Create();

        LibraryRecord? recorded = await library.RecordAsync(again, Cancellation);

        Assert.Empty(recorded!.Deleted);
        Assert.Empty(_site.Asked);
    }

    private static LibraryFile Stored(string id)
        => new(File(SkyUi, id), $"SkyUI {id}", null, ModFileCategory.Main, TestData.Now, $"{Folder}/SkyUI {id[0]}.{id[1]}.7z", 100, TestData.Now);

    private DownloadJobSnapshot Done(string fileId, string fileName)
        => RecordingDownloadService.Completed(RecordingDownloadService.Job(File(SkyUi, fileId), InRoot(Folder), fileName, DownloadJobStatus.Queued));

    private async Task<LibraryRecord> RecordAsync()
    {
        using LibraryDownloads library = Create();
        return (await library.RecordAsync(Done("53", "SkyUI 5.3.7z"), Cancellation))!;
    }

    private string InRoot(string relativePath) => Domain.Library.LibraryPaths.Resolve(Root, relativePath);

    private LibraryDownloads Create(LibraryChanges? changes = null)
        => new(new SourceRegistry([_site]), _downloads, _library, _settings, _storage, changes ?? new LibraryChanges(), _time, _logger);
}
