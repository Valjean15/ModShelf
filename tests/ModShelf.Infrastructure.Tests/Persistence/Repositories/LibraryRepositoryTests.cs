using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

public sealed class LibraryRepositoryTests : IAsyncLifetime
{
    private static readonly GameRef Stardew = new(new SourceId("nexus"), "stardewvalley");
    private static readonly ModRef Smapi = new(Stardew, "2400");
    private static readonly ModRef ContentPatcher = new(Stardew, "1915");
    private static readonly DateTimeOffset Added = new(2026, 10, 27, 6, 30, 5, 123, TimeSpan.FromHours(-6));

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AModAndItsFilesSurviveARestart()
    {
        LibraryFile main = File(Smapi, "160380", "nexus/stardewvalley/2400/SMAPI 4.5.2.zip");
        LibraryFile old = File(Smapi, "153075", "nexus/stardewvalley/2400/SMAPI 4.4.0.zip");
        LibraryMod mod = LibraryMod.Restore(
            Smapi, "SMAPI", "Pathoschild", new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"), isWatched: false, Added, Added.AddHours(5), [main, old]);
        await StoreAsync(mod);

        await _database.RestartAsync();

        LibraryMod stored = (await _database.Library.FindAsync(Smapi, Cancellation))!;
        Assert.Equal(
            (mod.Mod, mod.Name, mod.Author, mod.PageUrl, mod.IsWatched, mod.AddedAt, mod.CheckedAt),
            (stored.Mod, stored.Name, stored.Author, stored.PageUrl, stored.IsWatched, stored.AddedAt, stored.CheckedAt));
        Assert.Equal(Added.Offset, stored.AddedAt.Offset);
        Assert.Equal(2, stored.Files.Count);
        AssertSameFile(main, stored.Files[0]);
        AssertSameFile(old, stored.Files[1]);
    }

    [Fact]
    public async Task ListsTheModsInTheOrderTheyWereAdded()
    {
        await StoreAsync(Watched(Smapi));
        await StoreAsync(Watched(ContentPatcher));
        await StoreAsync(Watched(Smapi).Checked(Added));

        Assert.Equal([Smapi, ContentPatcher], (await _database.Library.ListAsync(Cancellation)).Select(mod => mod.Mod));
    }

    [Fact]
    public async Task FindsNothingForAModNotInTheLibrary()
    {
        await StoreAsync(Watched(Smapi));

        Assert.Null(await _database.Library.FindAsync(ContentPatcher, Cancellation));
        Assert.Null(await _database.Library.FindAsync(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "2400"), Cancellation));
    }

    [Fact]
    public async Task AChangeSaysWhatTheModWasBeforeAndAfter()
    {
        LibraryChange added = await _database.Library.UpdateAsync(Smapi, _ => Watched(Smapi), Cancellation);
        LibraryChange checkedNow = await _database.Library.UpdateAsync(Smapi, stored => stored!.Checked(Added), Cancellation);

        Assert.Null(added.Before);
        Assert.Equal(Smapi, added.After!.Mod);
        Assert.Null(checkedNow.Before!.CheckedAt);
        Assert.Equal(Added, checkedNow.After!.CheckedAt);
        Assert.Equal(Added, (await _database.Library.FindAsync(Smapi, Cancellation))!.CheckedAt);
    }

    [Fact]
    public async Task ReturningNullTakesTheModAndItsFilesOut()
    {
        await StoreAsync(Watched(Smapi).WithFile(File(Smapi, "160380", "a.zip")));
        await StoreAsync(Watched(ContentPatcher).WithFile(File(ContentPatcher, "1", "b.zip")));

        LibraryChange removed = await _database.Library.UpdateAsync(Smapi, _ => null, Cancellation);

        Assert.NotNull(removed.Before);
        Assert.Null(removed.After);
        Assert.Equal(ContentPatcher, Assert.Single(await _database.Library.ListAsync(Cancellation)).Mod);
        Assert.Equal(1L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryFiles"));
    }

    [Fact]
    public async Task RemovingAModNotInTheLibraryChangesNothing()
    {
        LibraryChange change = await _database.Library.UpdateAsync(Smapi, stored => stored, Cancellation);

        Assert.Equal(new LibraryChange(Smapi, null, null), change);
        Assert.Empty(await _database.Library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task FilesAreChangedInPlaceAddedAndRemoved()
    {
        await StoreAsync(Watched(Smapi).WithFile(File(Smapi, "1", "one.zip")).WithFile(File(Smapi, "2", "two.zip")));
        object? firstRow = await _database.QueryAsync("SELECT Sequence FROM LibraryFiles WHERE FileId = '1'");

        await _database.Library.UpdateAsync(
            Smapi,
            stored => stored!.WithFile(File(Smapi, "1", "old/one.zip")).WithoutFile(new ModFileRef(Smapi, "2"))!.WithFile(File(Smapi, "3", "three.zip")),
            Cancellation);

        LibraryMod stored = (await _database.Library.FindAsync(Smapi, Cancellation))!;
        Assert.Equal(["old/one.zip", "three.zip"], stored.Files.Select(file => file.RelativePath));
        Assert.Equal(firstRow, await _database.QueryAsync("SELECT Sequence FROM LibraryFiles WHERE FileId = '1'"));
        Assert.Equal(2L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryFiles"));
    }

    [Fact]
    public async Task AChangeThatFailsChangesNothing()
    {
        await StoreAsync(Watched(Smapi));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _database.Library.UpdateAsync(Smapi, _ => throw new InvalidOperationException("No."), Cancellation));

        Assert.Null((await _database.Library.FindAsync(Smapi, Cancellation))!.CheckedAt);
    }

    [Fact]
    public async Task AChangeThatReturnsAnotherModIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _database.Library.UpdateAsync(Smapi, _ => Watched(ContentPatcher), Cancellation));

        Assert.Empty(await _database.Library.ListAsync(Cancellation));
    }

    [Fact]
    public async Task ChangesMadeAtTheSameTimeAreAllKept()
    {
        // Each change counts one more in the name, from the name it reads: a change made between another's read and
        // write would be lost.
        await StoreAsync(LibraryMod.Watch(Smapi, "0", null, null, Added));

        await Task.WhenAll(Enumerable.Range(1, 20).Select(_ => Task.Run(
            () => _database.Library.UpdateAsync(
                Smapi,
                stored => stored!.WithDetails(Count(stored.Name, add: 1), null, null),
                Cancellation),
            Cancellation)));

        Assert.Equal("20", (await _database.Library.FindAsync(Smapi, Cancellation))!.Name);
    }

    [Fact]
    public async Task TheSameFileIdCanBeTwoModsFiles()
    {
        await StoreAsync(Watched(Smapi).WithFile(File(Smapi, "1", "a.zip")));
        await StoreAsync(Watched(ContentPatcher).WithFile(File(ContentPatcher, "1", "a.zip")));

        Assert.All(await _database.Library.ListAsync(Cancellation), mod => Assert.Equal("1", Assert.Single(mod.Files).File.Id));
    }

    [Fact]
    public async Task AStoredFileNoLibraryCanHaveStopsTheLoad()
    {
        await StoreAsync(Watched(Smapi).WithFile(File(Smapi, "1", "a.zip")));
        await _database.QueryAsync("UPDATE LibraryFiles SET RelativePath = '../outside.zip'");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => _database.Library.ListAsync(Cancellation));

        Assert.Contains("Mod 2400 of stardewvalley on nexus", exception.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => _database.Library.FindAsync(Smapi, Cancellation));
    }

    private static string Count(string name, int add = 0)
        => (int.Parse(name, System.Globalization.CultureInfo.InvariantCulture) + add).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static LibraryMod Watched(ModRef mod) => LibraryMod.Watch(mod, "Mod " + mod.Id, null, null, Added);

    private static LibraryFile File(ModRef mod, string id, string path)
        => new(new ModFileRef(mod, id), "File " + id, "1." + id, ModFileCategory.Main, Added.AddDays(-30), path, 41_889_142, Added.AddMinutes(3));

    private static void AssertSameFile(LibraryFile expected, LibraryFile actual)
        => Assert.Equal(
            (expected.File, expected.Name, expected.VersionLabel, expected.Category, expected.UploadedAt, expected.RelativePath, expected.SizeInBytes, expected.DownloadedAt),
            (actual.File, actual.Name, actual.VersionLabel, actual.Category, actual.UploadedAt, actual.RelativePath, actual.SizeInBytes, actual.DownloadedAt));

    private Task<LibraryChange> StoreAsync(LibraryMod mod) => _database.Library.UpdateAsync(mod.Mod, _ => mod, Cancellation);
}
