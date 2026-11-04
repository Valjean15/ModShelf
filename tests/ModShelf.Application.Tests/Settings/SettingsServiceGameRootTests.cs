using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Settings;

/// <summary>
/// A game's own folder in the settings: set while the game has no files, clear of the library's folder and every other
/// game's, and left alone by copying another game's settings; and the library's root, which games with folders of their
/// own don't hold back.
/// </summary>
public sealed class SettingsServiceGameRootTests : IDisposable
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "ModShelf.Tests");
    private static readonly string StardewFolder = Path.Combine(Base, "Stardew");
    private static readonly GameRef Stardew = new(new SourceId("nexus"), "stardewvalley");
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");

    private readonly InMemorySettingsStore _store = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemoryDownloadJobRepository _jobs = new([]);
    private readonly SettingsService _settings;

    public SettingsServiceGameRootTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, _jobs);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => _settings.DefaultLibraryRoot;

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task AGameWithoutFilesTakesAFolderOfItsOwnAsAFullPath()
    {
        _library.Add(LibraryMod.Watch(new ModRef(Stardew, "2400"), "SMAPI", null, null, DateTimeOffset.UnixEpoch));

        await _settings.SetGameAsync(Stardew, own => own with { Root = StardewFolder + Path.DirectorySeparatorChar }, Cancellation);
        LibraryRoots roots = await _settings.GetLibraryRootsAsync(Cancellation);

        Assert.Equal(StardewFolder, (await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root);
        Assert.Equal((StardewFolder, Root), (roots.For(Stardew), roots.For(Skyrim)));
    }

    [Fact]
    public async Task AGameWithFilesKeepsItsFolderUntilTheyMove()
    {
        _library.Add(WithFile(Stardew));

        InvalidOperationException set = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetGameAsync(Stardew, own => own with { Root = StardewFolder }, Cancellation));
        _store.Settings = Settings((Stardew, StardewFolder));
        InvalidOperationException reset = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetGameAsync(Stardew, own => own with { Root = null }, Cancellation));

        Assert.Equal($"nexus/stardewvalley has files in {Root}. Moving the game's files changes its folder and takes them along.", set.Message);
        Assert.Equal($"nexus/stardewvalley has files in {StardewFolder}. Moving the game's files changes its folder and takes them along.", reset.Message);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task AnotherGamesFilesDontKeepAGamesFolder()
    {
        _library.Add(WithFile(Skyrim));

        await _settings.SetGameAsync(Stardew, own => own with { Root = StardewFolder, Notify = false }, Cancellation);

        Assert.Equal(new GameSettings { Root = StardewFolder, Notify = false }, (await _settings.GetAsync(Cancellation)).GameFor(Stardew));
    }

    [Fact]
    public async Task OtherSettingsOfAGameWithFilesStillChange()
    {
        _library.Add(WithFile(Stardew));
        _store.Settings = Settings((Stardew, StardewFolder));

        await _settings.SetGameAsync(Stardew, own => own with { Hidden = true }, Cancellation);
        await _settings.SetGameAsync(Stardew, own => own with { Root = StardewFolder + Path.DirectorySeparatorChar }, Cancellation);

        Assert.Equal(new GameSettings { Root = StardewFolder, Hidden = true }, (await _settings.GetAsync(Cancellation)).GameFor(Stardew));
    }

    [Fact]
    public async Task AGamesFolderIsClearOfTheLibrarysAndOtherGames()
    {
        _store.Settings = Settings((Skyrim, Path.Combine(Base, "Skyrim")));

        InvalidOperationException inLibrary = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetGameAsync(Stardew, own => own with { Root = Path.Combine(Root, "Stardew") }, Cancellation));
        InvalidOperationException inSkyrims = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetGameAsync(Stardew, own => own with { Root = Path.Combine(Base, "Skyrim", "Stardew") }, Cancellation));

        Assert.Equal($"{Path.Combine(Root, "Stardew")} is inside the library's folder, {Root}. Choose a folder outside it.", inLibrary.Message);
        Assert.Equal(
            $"{Path.Combine(Base, "Skyrim", "Stardew")} is inside nexus/skyrimspecialedition's folder, {Path.Combine(Base, "Skyrim")}. Choose a folder outside it.",
            inSkyrims.Message);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task UnfinishedDownloadsOfTheGameIntoItsFolderKeepItButNotAnotherGames()
    {
        await _jobs.AddAsync(Job(Stardew, Path.Combine(Root, "nexus")), Cancellation);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetGameAsync(Stardew, own => own with { Root = StardewFolder }, Cancellation));
        await _settings.SetGameAsync(Skyrim, own => own with { Root = Path.Combine(Base, "Skyrim") }, Cancellation);

        Assert.Equal($"A download into {Root} isn't finished. Let it finish, or cancel it, first.", refused.Message);
    }

    [Fact]
    public async Task RefusesAFolderThatIsntAFullPath()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetGameAsync(Stardew, own => own with { Root = "Stardew" }, Cancellation));
    }

    [Fact]
    public async Task CopyingAnotherGamesSettingsKeepsTheGamesOwnFolder()
    {
        GameRef example = new(new SourceId("example"), "stardew-valley");
        _store.Settings = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer)
            {
                [example] = new GameSettings { Root = Path.Combine(Base, "Example"), Notify = false },
                [Stardew] = new GameSettings { Root = StardewFolder, Hidden = true },
            },
        };

        await _settings.CopyGameAsync(example, Stardew, Cancellation);

        Assert.Equal(new GameSettings { Root = StardewFolder, Notify = false }, (await _settings.GetAsync(Cancellation)).GameFor(Stardew));
    }

    [Fact]
    public async Task TheLibrarysRootIsHeldOnlyByTheFilesInIt()
    {
        _library.Add(WithFile(Stardew));
        _store.Settings = Settings((Stardew, StardewFolder));
        string mods = Path.Combine(Base, "Mods");

        await _settings.SetLibraryRootAsync(mods, Cancellation);

        Assert.Equal(mods, await _settings.GetLibraryRootAsync(Cancellation));
    }

    [Fact]
    public async Task TheLibrarysRootIsClearOfGamesFolders()
    {
        _store.Settings = Settings((Stardew, StardewFolder));

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _settings.SetLibraryRootAsync(Base, Cancellation));

        Assert.Equal($"{Base} holds nexus/stardewvalley's folder, {StardewFolder}. Choose a folder that doesn't.", refused.Message);
    }

    [Fact]
    public async Task MovingAGameSetsItsFolderWhateverItHolds()
    {
        _library.Add(WithFile(Stardew));

        await _settings.MoveGameRootAsync(Stardew, StardewFolder + Path.DirectorySeparatorChar, Cancellation);
        string? moved = (await _settings.GetAsync(Cancellation)).GameFor(Stardew).Root;
        await _settings.MoveGameRootAsync(Stardew, null, Cancellation);

        Assert.Equal(StardewFolder, moved);
        Assert.Empty((await _settings.GetAsync(Cancellation)).Games);
    }

    private static AppSettings Settings(params (GameRef Game, string Root)[] games)
        => new() { Games = games.ToDictionary(game => game.Game, game => new GameSettings { Root = game.Root }, AppSettings.GameComparer) };

    private static LibraryMod WithFile(GameRef game)
    {
        ModRef mod = new(game, "1");
        return LibraryMod.Watch(mod, "A mod", null, null, DateTimeOffset.UnixEpoch).WithFile(new LibraryFile(
            new ModFileRef(mod, "10"), "A mod 1.0", "1.0", ModFileCategory.Main, DateTimeOffset.UnixEpoch, "A mod (1)/a.zip", 1, DateTimeOffset.UnixEpoch));
    }

    private static DownloadJob Job(GameRef game, string folder)
        => DownloadJob.Create(new ModFileRef(new ModRef(game, "1"), "10"), folder, "a.zip", DateTimeOffset.UnixEpoch);
}
