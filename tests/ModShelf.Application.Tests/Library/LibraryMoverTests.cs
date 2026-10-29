using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// Moving the library, on a disk the test sets up: SkyUI's two files, one archived, and a file of the user's own.
/// </summary>
public sealed class LibraryMoverTests : IDisposable
{
    private const string SkyUiFile = "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI 5.2.7z";
    private const string ArchivedFile = "nexus/skyrimspecialedition/SkyUI (12604)/_archive/SkyUI 5.1.7z";

    private static readonly string Mods = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Mods");

    private readonly InMemorySettingsStore _store = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemoryDownloadJobRepository _jobs = new([]);
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<LibraryMover> _logger = new();
    private readonly SettingsService _settings;

    public LibraryMoverTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, _jobs);
        LibraryMod mod = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now)
            .WithFile(Stored("35407", SkyUiFile, 100))
            .WithFile(Stored("35406", ArchivedFile, 90));
        _library.Add(mod);
        _storage.Add(Old(SkyUiFile), 100);
        _storage.Add(Old(ArchivedFile), 90);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ModRef SkyUi => TestData.File(TestData.Nexus, "35407").Mod;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task MovesEveryFileThenTheRoot()
    {
        LibraryMove move = await Mover().MoveAsync(Mods + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(new LibraryMove(Root, Mods, 2, 0, 0), move);
        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
        Assert.Equal([New(SkyUiFile), New(ArchivedFile)], _storage.Files.Order(StringComparer.Ordinal));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith($"Information: Moved the library from {Root} to {Mods}: 2 files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovesTheFoldersItEmptiedTheRootToo()
    {
        await Mover().MoveAsync(Mods, Cancellation);

        string mod = Old("nexus/skyrimspecialedition/SkyUI (12604)");
        Assert.Equal(
            [Old(ArchivedFile)[..^"/SkyUI 5.1.7z".Length], mod, Old("nexus/skyrimspecialedition"), Old("nexus"), Root],
            _storage.RemovedFolders);
    }

    [Fact]
    public async Task LeavesTheUsersOwnFilesAndTheirFolders()
    {
        _storage.Add(Old("nexus/notes.txt"));

        await Mover().MoveAsync(Mods, Cancellation);

        Assert.Contains(Old("nexus/notes.txt"), _storage.Files);
        Assert.DoesNotContain(Old("nexus"), _storage.RemovedFolders);
        Assert.DoesNotContain(Root, _storage.RemovedFolders);
    }

    [Fact]
    public async Task MovingToTheRootItHasChangesNothing()
    {
        LibraryMove move = await Mover().MoveAsync(Root.ToUpperInvariant() is var upper && OperatingSystem.IsLinux() ? Root : upper, Cancellation);

        Assert.Equal((0, 0, 0), (move.Moved, move.AlreadyThere, move.Missing));
        Assert.Equal(move.From, move.To);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task RefusesARootInsideTheLibraryOrHoldingIt()
    {
        InvalidOperationException inside = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveAsync(Path.Combine(Root, "Mods"), Cancellation));
        InvalidOperationException holding = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveAsync(Path.GetDirectoryName(Root)!, Cancellation));

        Assert.Equal($"{Path.Combine(Root, "Mods")} is inside the library, which is in {Root}. Choose a folder outside it.", inside.Message);
        Assert.Equal($"{Path.GetDirectoryName(Root)} holds the library, which is in {Root}. Choose a folder that doesn't.", holding.Message);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task RefusesARelativeRoot()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Mover().MoveAsync("Mods", Cancellation));
    }

    [Fact]
    public async Task WaitsForDownloadsIntoTheLibrary()
    {
        await _jobs.AddAsync(DownloadJob.Create(TestData.File(TestData.Nexus, "1"), Old("nexus"), "SkyUI.7z", TestData.Now), Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.Equal($"A download into {Root} isn't finished. Let it finish, or cancel it, first.", refused.Message);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task NeverMovesOverAnotherFile()
    {
        _storage.Add(New(ArchivedFile), 90);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.StartsWith($"A file is at {New(ArchivedFile)}, where one of the library's would go.", refused.Message, StringComparison.Ordinal);
        Assert.Empty(_storage.Moves);
        Assert.Equal(Root, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task AFileOfAnotherSizeWhereOneWentIsInTheWay()
    {
        _storage.Remove(Old(ArchivedFile));
        _storage.Add(New(ArchivedFile), 91);
        _storage.Add(New("nexus/other.7z"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task FinishesAMoveThatStoppedPartWay()
    {
        _storage.Remove(Old(ArchivedFile));
        _storage.Add(New(ArchivedFile), 90);

        LibraryMove move = await Mover().MoveAsync(Mods, Cancellation);

        Assert.Equal((1, 1, 0), (move.Moved, move.AlreadyThere, move.Missing));
        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task FilesGoneFromDiskStayInTheLibrary()
    {
        _storage.Remove(Old(ArchivedFile));

        LibraryMove move = await Mover().MoveAsync(Mods, Cancellation);

        Assert.Equal((1, 0, 1), (move.Moved, move.AlreadyThere, move.Missing));
        Assert.Equal(2, Assert.Single(_library.Mods).Files.Count);
    }

    [Fact]
    public async Task PutsEveryFileBackWhenOneCantMove()
    {
        _storage.Failures[Old(ArchivedFile)] = new IOException("In use.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.Equal(
            $"{Old(ArchivedFile)} couldn't be moved: In use. Every file is back where it was, and the library stays in {Root}.",
            refused.Message);
        Assert.Equal([Old(SkyUiFile), Old(ArchivedFile)], _storage.Files.Order(StringComparer.Ordinal));
        Assert.Equal(Root, await _settings.GetLibraryRootAsync(Cancellation));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith($"Warning: Couldn't move {Old(ArchivedFile)}, so the 1 files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaysWhenFilesCouldntGoBack()
    {
        _storage.Failures[Old(ArchivedFile)] = new IOException("In use.");
        _storage.Failures[New(SkyUiFile)] = new IOException("Disk gone.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.EndsWith($"1 file couldn't go back, so it's in {Mods}. Move the library there again to finish the move.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(Root, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task PutsEveryFileBackWhenTheNewRootCantBeSaved()
    {
        _store.SaveFailure = new IOException("The settings couldn't be saved to memory: The disk is full.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.Equal(
            $"The library's new root couldn't be saved: The settings couldn't be saved to memory: The disk is full. Every file is back where it was, and the library stays in {Root}.",
            refused.Message);
        Assert.Equal([Old(SkyUiFile), Old(ArchivedFile)], _storage.Files.Order(StringComparer.Ordinal));
        Assert.Equal(Root, await _settings.GetLibraryRootAsync(Cancellation));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith($"Warning: Couldn't save the library's new root {Mods}, so the 2 files moved went back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaysWhenFilesCouldntGoBackAfterTheNewRootCouldntBeSaved()
    {
        _store.SaveFailure = new IOException("The settings couldn't be saved to memory: The disk is full.");
        _storage.Failures[New(SkyUiFile)] = new IOException("Disk gone.");
        _storage.Failures[New(ArchivedFile)] = new IOException("Disk gone.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveAsync(Mods, Cancellation));

        Assert.EndsWith($"2 files couldn't go back, so they're in {Mods}. Move the library there again to finish the move.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingLeavesWhatMovedForTheNextMove()
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        _storage.AfterMove = _ => stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mover().MoveAsync(Mods, stop.Token));
        _storage.AfterMove = null;
        LibraryMove finished = await Mover().MoveAsync(Mods, Cancellation);

        Assert.Equal((1, 1), (finished.Moved, finished.AlreadyThere));
        Assert.Equal(Mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    private static LibraryFile Stored(string id, string relativePath, long size)
        => new(new ModFileRef(SkyUi, id), $"SkyUI {id}", null, ModFileCategory.Main, TestData.Now, relativePath, size, TestData.Now);

    private static string New(string relativePath) => LibraryPaths.Resolve(Mods, relativePath);

    private string Old(string relativePath) => LibraryPaths.Resolve(Root, relativePath);

    private LibraryMover Mover() => new(_settings, _library, _storage, _logger);
}
