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
    public async Task WhetherAModNeedsAFullCheckSurvivesARestartAndAChange()
    {
        LibraryFile main = File(Smapi, "160380", "nexus/stardewvalley/2400/SMAPI 4.5.2.zip");
        await StoreAsync(LibraryMod.Restore(Smapi, "SMAPI", null, null, isWatched: false, Added, Added.AddHours(5), [main]).Watched());

        await _database.RestartAsync();
        bool stored = (await _database.Library.FindAsync(Smapi, Cancellation))!.NeedsFullCheck;
        await _database.Library.UpdateAsync(Smapi, mod => mod!.Checked(Added.AddDays(1)), Cancellation);
        await _database.RestartAsync();

        Assert.True(stored);
        Assert.False((await _database.Library.FindAsync(Smapi, Cancellation))!.NeedsFullCheck);
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

    [Fact]
    public async Task UpdatesSurviveARestartInTheirOrder()
    {
        LibraryMod mod = Watched(Smapi)
            .WithFile(File(Smapi, "153075", "a.zip"))
            .Checked(Added.AddHours(1))
            .WithUpdates([Update(Smapi, "160380", replaces: "153075"), Update(Smapi, "160381")]);
        await StoreAsync(mod);

        await _database.RestartAsync();

        LibraryMod stored = (await _database.Library.FindAsync(Smapi, Cancellation))!;
        Assert.Equal(["160380", "160381"], stored.Updates.Select(update => update.File.Id));
        AssertSameUpdate(mod.Updates[0], stored.Updates[0]);
        AssertSameUpdate(mod.Updates[1], stored.Updates[1]);
        Assert.Equal(Added.AddHours(1), stored.CheckedAt);
    }

    [Fact]
    public async Task TheListHasEachModsUpdates()
    {
        await StoreAsync(Watched(Smapi).WithUpdates([Update(Smapi, "1")]));

        LibraryMod listed = Assert.Single(await _database.Library.ListAsync(Cancellation));

        Assert.Equal("1", Assert.Single(listed.Updates).File.Id);
    }

    [Fact]
    public async Task UpdatesAreChangedInPlaceAddedRemovedAndReordered()
    {
        await StoreAsync(Watched(Smapi).WithUpdates([Update(Smapi, "1"), Update(Smapi, "2")]));

        await _database.Library.UpdateAsync(
            Smapi,
            mod => mod!.WithUpdates([Update(Smapi, "3"), Update(Smapi, "2", name: "Renamed")]),
            Cancellation);

        LibraryMod stored = (await _database.Library.FindAsync(Smapi, Cancellation))!;
        Assert.Equal(["3", "2"], stored.Updates.Select(update => update.File.Id));
        Assert.Equal("Renamed", stored.Updates[1].Name);
        Assert.Equal(Added, stored.Updates[1].FoundAt);
        Assert.Equal(2L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryUpdates"));
    }

    [Fact]
    public async Task DownloadingAnUpdateTakesItsRowOff()
    {
        await StoreAsync(Watched(Smapi).WithFile(File(Smapi, "1", "a.zip")).WithUpdates([Update(Smapi, "2", replaces: "1")]));

        await _database.Library.UpdateAsync(Smapi, mod => mod!.WithFile(File(Smapi, "2", "b.zip")), Cancellation);

        Assert.Empty((await _database.Library.FindAsync(Smapi, Cancellation))!.Updates);
        Assert.Equal(0L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryUpdates"));
    }

    [Fact]
    public async Task AModsUpdatesGoWithIt()
    {
        await StoreAsync(Watched(Smapi).WithUpdates([Update(Smapi, "1")]));

        await _database.Library.UpdateAsync(Smapi, mod => mod!.Unwatched(), Cancellation);

        Assert.Equal(0L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryUpdates"));
    }

    [Fact]
    public async Task AStoredUpdateNoLibraryCanHaveStopsTheLoad()
    {
        await StoreAsync(Watched(Smapi).WithUpdates([Update(Smapi, "1")]));
        await _database.QueryAsync("UPDATE LibraryUpdates SET ReplacesFileId = '99'");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => _database.Library.FindAsync(Smapi, Cancellation));

        Assert.Contains("Mod 2400 of stardewvalley on nexus", exception.Message, StringComparison.Ordinal);
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

    private static LibraryUpdate Update(ModRef mod, string id, string? replaces = null, string? name = null)
        => new(
            new ModFileRef(mod, id),
            name ?? "SMAPI " + id,
            $"SMAPI {id}.zip",
            "4.5." + id,
            ModFileCategory.Main,
            Added.AddDays(-1),
            replaces is null ? null : new ModFileRef(mod, replaces),
            Added,
            new Uri($"https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id={id}"));

    private static void AssertSameUpdate(LibraryUpdate expected, LibraryUpdate actual)
        => Assert.Equal(
            (expected.File, expected.Name, expected.FileName, expected.VersionLabel, expected.Category, expected.UploadedAt, expected.Replaces, expected.FoundAt, expected.PageUrl),
            (actual.File, actual.Name, actual.FileName, actual.VersionLabel, actual.Category, actual.UploadedAt, actual.Replaces, actual.FoundAt, actual.PageUrl));

    private Task<LibraryChange> StoreAsync(LibraryMod mod) => _database.Library.UpdateAsync(mod.Mod, _ => mod, Cancellation);
}
