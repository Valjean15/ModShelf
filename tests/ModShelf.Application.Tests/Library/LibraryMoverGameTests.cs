using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// Moving a game's files to a folder of its own and back, on a disk the test sets up: Stardew Valley's SMAPI with two
/// files, one archived, and Skyrim's SkyUI with one, all in the library's folder.
/// </summary>
public sealed class LibraryMoverGameTests : IDisposable
{
    private const string SmapiFile = "nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.zip";
    private const string ArchivedFile = "nexus/stardewvalley/SMAPI (2400)/_archive/SMAPI 4.4.zip";
    private const string SkyUiFile = "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI 5.2.7z";

    private static readonly string Games = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Games");
    private static readonly string StardewFolder = Path.Combine(Games, "Stardew");
    private static readonly GameRef Stardew = new(new SourceId("nexus"), "stardewvalley");
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");

    private readonly InMemorySettingsStore _store = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemoryDownloadJobRepository _jobs = new([]);
    private readonly FakeLibraryStorage _storage = new();
    private readonly FakeLibraryHold _hold = new();
    private readonly RecordingLogger<LibraryMover> _logger = new();
    private readonly SettingsService _settings;

    public LibraryMoverGameTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, _jobs);
        ModRef smapi = new(Stardew, "2400");
        ModRef skyUi = new(Skyrim, "12604");
        _library.Add(LibraryMod.Watch(smapi, "SMAPI", null, null, TestData.Now)
            .WithFile(Stored(smapi, "2", SmapiFile, 100))
            .WithFile(Stored(smapi, "1", ArchivedFile, 90)));
        _library.Add(LibraryMod.Watch(skyUi, "SkyUI", null, null, TestData.Now).WithFile(Stored(skyUi, "3", SkyUiFile, 50)));
        _storage.Add(InLibrary(SmapiFile), 100);
        _storage.Add(InLibrary(ArchivedFile), 90);
        _storage.Add(InLibrary(SkyUiFile), 50);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task SaysWhatMovingAGameWouldDoMovingNothing()
    {
        _storage.Free[string.Empty] = 5000;

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, StardewFolder + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(new GameMovePlan(Stardew, Root, StardewFolder, 2, 190, 0, 0, 5000, SameDrive: true), plan);
        Assert.True(plan.Fits);
        Assert.Empty(_storage.Moves);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task SaysWhenTheFilesGoToAnotherDriveAndWhetherTheyFit()
    {
        _storage.Drives.Add(Games);
        _storage.Free[Games] = 150;

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, StardewFolder, Cancellation);
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation));

        Assert.Equal((false, 150L, false), (plan.SameDrive, plan.FreeSpace, plan.Fits));
        Assert.Equal(
            $"nexus/stardewvalley's files take 190 bytes, and the drive that holds {StardewFolder} has 150 bytes free. Make room there, or choose another folder.",
            refused.Message);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task OnTheSameDriveFilesFitWhateverTheRoom()
    {
        _storage.Free[string.Empty] = 10;

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, StardewFolder, Cancellation);
        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        Assert.Equal((true, 10L, true), (plan.SameDrive, plan.FreeSpace, plan.Fits));
        Assert.Contains(InGame(SmapiFile), _storage.Files);
    }

    [Fact]
    public async Task FilesWhoseRoomIsntKnownFit()
    {
        _storage.Drives.Add(Games);

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, StardewFolder, Cancellation);

        Assert.Equal((false, null, true), (plan.SameDrive, plan.FreeSpace, plan.Fits));
    }

    [Fact]
    public async Task MovesTheGamesFilesThenItsFolderTellingHowFarItIs()
    {
        List<LibraryMoveProgress> progress = [];

        LibraryMove move = await Mover().MoveGameAsync(Stardew, StardewFolder, new Recorded<LibraryMoveProgress>(progress), Cancellation);

        Assert.Equal(new LibraryMove(Root, StardewFolder, 2, 0, 0), move);
        Assert.Equal(StardewFolder, (await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
        Assert.Equal(new[] { InLibrary(SkyUiFile), InGame(ArchivedFile), InGame(SmapiFile) }.Order(StringComparer.Ordinal), _storage.Files.Order(StringComparer.Ordinal));
        Assert.Equal([new(0, 2, 0, 190), new(1, 2, 100, 190), new(2, 2, 190, 190)], progress);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith($"Information: Moved nexus/stardewvalley's files from {Root} to {StardewFolder}: 2 files", StringComparison.Ordinal));
        Assert.False(_hold.IsHeld);
        Assert.Equal(1, _hold.Holds);
    }

    [Fact]
    public async Task RemovesTheFoldersItEmptiedButNotTheLibrarys()
    {
        _storage.Remove(InLibrary(SkyUiFile));

        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        Assert.Contains(InLibrary("nexus/stardewvalley"), _storage.RemovedFolders);
        Assert.Contains(InLibrary("nexus"), _storage.RemovedFolders);
        Assert.DoesNotContain(Root, _storage.RemovedFolders);
    }

    [Fact]
    public async Task MovesTheFilesBackIntoTheLibraryRemovingTheGamesEmptiedFolder()
    {
        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        LibraryMove back = await Mover().MoveGameAsync(Stardew, null, null, Cancellation);

        Assert.Equal(new LibraryMove(StardewFolder, Root, 2, 0, 0), back);
        Assert.Empty((await _settings.GetAsync(Cancellation)).Games);
        Assert.Contains(InLibrary(SmapiFile), _storage.Files);
        Assert.Contains(StardewFolder, _storage.RemovedFolders);
    }

    [Fact]
    public async Task TheLibrarysFolderNamedAsAGamesIsTheLibrarys()
    {
        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, Root, Cancellation);
        await Mover().MoveGameAsync(Stardew, Root, null, Cancellation);

        Assert.Equal((StardewFolder, Root), (plan.From, plan.To));
        Assert.Null((await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
    }

    [Fact]
    public async Task MovingToTheGamesFolderChangesNothing()
    {
        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, null, Cancellation);
        LibraryMove move = await Mover().MoveGameAsync(Stardew, Root, null, Cancellation);

        Assert.Equal((Root, Root, 0), (plan.From, plan.To, plan.Files));
        Assert.Equal(new LibraryMove(Root, Root, 0, 0, 0), move);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task AdoptsFilesInTheNewFolderAtTheirSizeKeepingTheOldCopies()
    {
        _storage.Add(InGame(SmapiFile), 100);

        GameMovePlan plan = await Mover().PlanGameMoveAsync(Stardew, StardewFolder, Cancellation);
        LibraryMove move = await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        Assert.Equal((1, 90L, 1, 1), (plan.Files, plan.Bytes, plan.AlreadyThere, plan.CopiesKept));
        Assert.Equal(new LibraryMove(Root, StardewFolder, 1, 1, 0, 1), move);
        Assert.Contains(InLibrary(SmapiFile), _storage.Files);
        Assert.Equal([(InLibrary(ArchivedFile), InGame(ArchivedFile))], _storage.Moves);
    }

    [Fact]
    public async Task FinishesAMoveThatStoppedWithTheAppAsked()
    {
        // As the app or the computer stopping leaves it: one file moved, the folder not changed yet.
        _storage.Remove(InLibrary(SmapiFile));
        _storage.Add(InGame(SmapiFile), 100);

        LibraryMove move = await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        Assert.Equal(new LibraryMove(Root, StardewFolder, 1, 1, 0), move);
    }

    [Fact]
    public async Task AnotherFileWhereOneOfTheGamesWouldGoStopsIt()
    {
        _storage.Add(InGame(SmapiFile), 7);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().PlanGameMoveAsync(Stardew, StardewFolder, Cancellation));

        Assert.Equal(
            $"A file is at {InGame(SmapiFile)}, where one of nexus/stardewvalley's would go, and it isn't the same size. Move or delete it, then move nexus/stardewvalley again.",
            refused.Message);
    }

    [Fact]
    public async Task CountsTheFilesThatArentOnDisk()
    {
        _storage.Remove(InLibrary(ArchivedFile));

        LibraryMove move = await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        Assert.Equal(new LibraryMove(Root, StardewFolder, 1, 0, 1), move);
    }

    [Fact]
    public async Task RefusesTheLibrarysFolderOrAnotherGamesOrOneInsideThem()
    {
        _store.Settings = new AppSettings { Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [Skyrim] = new() { Root = Path.Combine(Games, "Skyrim") } } };

        InvalidOperationException inLibrary = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveGameAsync(Stardew, Path.Combine(Root, "Stardew"), null, Cancellation));
        InvalidOperationException skyrims = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().PlanGameMoveAsync(Stardew, Path.Combine(Games, "Skyrim"), Cancellation));

        Assert.Equal($"{Path.Combine(Root, "Stardew")} is inside the library's folder, {Root}. Choose a folder outside it.", inLibrary.Message);
        Assert.Equal($"{Path.Combine(Games, "Skyrim")} is nexus/skyrimspecialedition's folder. Choose another folder.", skyrims.Message);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task UnfinishedDownloadsOfTheGameKeepItWhereItIs()
    {
        await _jobs.AddAsync(DownloadJob.Create(new ModFileRef(new ModRef(Stardew, "2400"), "3"), InLibrary("nexus/stardewvalley/SMAPI (2400)"), "SMAPI 4.6.zip", TestData.Now), Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation));

        Assert.Equal($"A download into {Root} isn't finished. Let it finish, or cancel it, first.", refused.Message);
        Assert.Empty(_storage.Moves);
    }

    [Fact]
    public async Task StoppingPutsBackEveryFileMoved()
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        _storage.AfterMove = _ => stop.Cancel();

        OperationCanceledException stopped = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Mover().MoveGameAsync(Stardew, StardewFolder, null, stop.Token));

        Assert.Equal($"The move was stopped. Every file is back where it was, and nexus/stardewvalley stays in {Root}.", stopped.Message);
        Assert.Equal(new[] { InLibrary(ArchivedFile), InLibrary(SkyUiFile), InLibrary(SmapiFile) }.Order(StringComparer.Ordinal), _storage.Files.Order(StringComparer.Ordinal));
        Assert.Null((await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
        Assert.Contains(_logger.Entries, entry => entry.Contains("The move of nexus/stardewvalley's files was stopped, so the 1 files moved went back", StringComparison.Ordinal));
        Assert.False(_hold.IsHeld);
    }

    [Fact]
    public async Task ADownloadOfTheGameStartedWhileItMovedPutsBackTheFiles()
    {
        // As another process, such as the desktop app, may start one into the old folder meanwhile.
        _storage.AfterMove = _ =>
        {
            _storage.AfterMove = null;
            _jobs.AddAsync(DownloadJob.Create(new ModFileRef(new ModRef(Stardew, "2400"), "3"), InLibrary("nexus/stardewvalley/SMAPI (2400)"), "SMAPI 4.6.zip", TestData.Now), Cancellation)
                .GetAwaiter().GetResult();
        };

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation));

        Assert.Equal(
            $"A download into {Root} isn't finished. Let it finish, or cancel it, first. Every file is back where it was, and nexus/stardewvalley stays in {Root}.",
            refused.Message);
        Assert.Contains(InLibrary(SmapiFile), _storage.Files);
        Assert.Null((await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
    }

    [Fact]
    public async Task AFileThatCantMovePutsBackTheOnesMoved()
    {
        _storage.Failures[InLibrary(SmapiFile)] = new IOException("It's open in another program.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation));

        Assert.Equal(
            $"{InLibrary(SmapiFile)} couldn't be moved: It's open in another program. Every file is back where it was, and nexus/stardewvalley stays in {Root}.",
            refused.Message);
        Assert.DoesNotContain(InGame(ArchivedFile), _storage.Files);
        Assert.Null((await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
    }

    [Fact]
    public async Task SettingsThatCantBeSavedPutBackTheFiles()
    {
        _store.SaveFailure = new IOException("The disk is full.");

        IOException refused = await Assert.ThrowsAsync<IOException>(() => Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation));

        Assert.Equal($"nexus/stardewvalley's new folder couldn't be saved: The disk is full. Every file is back where it was, and nexus/stardewvalley stays in {Root}.", refused.Message);
        Assert.Contains(InLibrary(SmapiFile), _storage.Files);
    }

    [Fact]
    public async Task MovingTheLibraryLeavesAGameWithAFolderOfItsOwn()
    {
        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);
        string mods = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Mods");

        LibraryMove move = await Mover().MoveAsync(mods, Cancellation);

        Assert.Equal(new LibraryMove(Root, mods, 1, 0, 0), move);
        Assert.Contains(InGame(SmapiFile), _storage.Files);
        Assert.Contains(LibraryPaths.Resolve(mods, SkyUiFile), _storage.Files);
    }

    [Fact]
    public async Task TheLibrarysNewRootIsClearOfGamesFolders()
    {
        await Mover().MoveGameAsync(Stardew, StardewFolder, null, Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Mover().MoveAsync(Games, Cancellation));

        Assert.Equal($"{Games} holds nexus/stardewvalley's folder, {StardewFolder}. Choose a folder that doesn't.", refused.Message);
    }

    [Fact]
    public async Task RefusesAFolderThatIsntAFullPath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Mover().PlanGameMoveAsync(Stardew, "Stardew", Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => Mover().MoveGameAsync(Stardew, "Stardew", null, Cancellation));
    }

    private static LibraryFile Stored(ModRef mod, string id, string relativePath, long size)
        => new(new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, TestData.Now, relativePath, size, TestData.Now);

    private static string InGame(string relativePath) => LibraryPaths.Resolve(StardewFolder, relativePath);

    private string InLibrary(string relativePath) => LibraryPaths.Resolve(Root, relativePath);

    private LibraryMover Mover() => new(_settings, _library, _storage, _hold, _logger);

    /// <summary>
    /// Progress kept as it's told, at once, rather than posted as <see cref="Progress{T}"/> posts it.
    /// </summary>
    private sealed class Recorded<T>(List<T> told) : IProgress<T>
    {
        public void Report(T value) => told.Add(value);
    }
}
