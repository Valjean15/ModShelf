using ModShelf.Application.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// The library's history in the database: kept with every value, listed the newest first, and pruned.
/// </summary>
public sealed class LibraryHistoryRepositoryTests : IAsyncLifetime
{
    private static readonly ModRef SkyUi = new(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604");

    // Sub-millisecond ticks and a non-UTC offset, to prove the time comes back as the same instant.
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 3, 9, 30, 15, TimeSpan.FromHours(-6)).AddTicks(1234567);

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AnEmptyLibraryHasNoHistory()
    {
        Assert.Empty(await _database.History.ListAsync(null, 10, Cancellation));
    }

    [Fact]
    public async Task AnEventSurvivesARestartWithEveryValue()
    {
        LibraryEvent downloaded = new(
            Now,
            LibraryEventKind.Downloaded,
            SkyUi,
            "SkyUI (Ünïcode)",
            new ModFileRef(SkyUi, "35408"),
            "SkyUI 5.3",
            "5.3",
            "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_3_SE.7z",
            new ModFileRef(SkyUi, "35407"),
            "5.2");
        LibraryEvent deleted = new(
            Now, LibraryEventKind.Deleted, SkyUi, "SkyUI", new ModFileRef(SkyUi, "35406"), "SkyUI 5.1", null, "nexus/x/SkyUI_5_1.7z", ByRule: true);
        LibraryEvent removed = new(Now, LibraryEventKind.Removed, SkyUi, "SkyUI");
        await _database.History.AddAsync([downloaded, deleted, removed], Now.AddYears(-1), Cancellation);

        await _database.RestartAsync();

        Assert.Equal([downloaded, deleted, removed], await _database.History.ListAsync(null, 10, Cancellation));
        Assert.Equal(Now.UtcTicks, (await _database.History.ListAsync(null, 1, Cancellation))[0].At.UtcTicks);
    }

    [Fact]
    public async Task KeepsTheKindByName()
    {
        await _database.History.AddAsync([Event(LibraryEventKind.Archived, "1", Now)], Now.AddYears(-1), Cancellation);

        Assert.Equal("Archived", await _database.QueryAsync("SELECT Kind FROM LibraryEvents"));
    }

    [Fact]
    public async Task ListsTheNewestFirstAndWhatHappenedTogetherInTheOrderItWasKept()
    {
        await _database.History.AddAsync([Event(LibraryEventKind.Downloaded, "1", Now.AddHours(-1))], Now.AddYears(-1), Cancellation);
        await _database.History.AddAsync(
            [Event(LibraryEventKind.Downloaded, "3", Now), Event(LibraryEventKind.Deleted, "2", Now)],
            Now.AddYears(-1),
            Cancellation);

        // A minute later, in another offset, is the newest.
        await _database.History.AddAsync(
            [Event(LibraryEventKind.Found, "4", Now.ToOffset(TimeSpan.FromHours(9)).AddMinutes(1))],
            Now.AddYears(-1),
            Cancellation);

        Assert.Equal(["4", "3", "2", "1"], (await _database.History.ListAsync(null, 10, Cancellation)).Select(happened => happened.File!.Id));
    }

    [Fact]
    public async Task ListsWhatHappenedSinceATimeAtMostTheLimit()
    {
        await _database.History.AddAsync(
            [
                Event(LibraryEventKind.Downloaded, "1", Now.AddDays(-2)),
                Event(LibraryEventKind.Downloaded, "2", Now.AddDays(-1)),
                Event(LibraryEventKind.Downloaded, "3", Now),
            ],
            Now.AddYears(-1),
            Cancellation);

        Assert.Equal(["3", "2"], (await _database.History.ListAsync(Now.AddDays(-1), 10, Cancellation)).Select(happened => happened.File!.Id));
        Assert.Equal(["3", "2"], (await _database.History.ListAsync(null, 2, Cancellation)).Select(happened => happened.File!.Id));
        Assert.Empty(await _database.History.ListAsync(Now.AddTicks(1), 10, Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _database.History.ListAsync(null, 0, Cancellation));
    }

    [Fact]
    public async Task AddingForgetsWhatHappenedBeforeTheTimeToKeepFrom()
    {
        await _database.History.AddAsync(
            [
                Event(LibraryEventKind.Downloaded, "1", Now.AddDays(-400)),
                Event(LibraryEventKind.Downloaded, "0", Now.AddDays(-300).AddMinutes(-1)),
                Event(LibraryEventKind.Downloaded, "2", Now.AddDays(-300)),
            ],
            Now.AddDays(-500),
            Cancellation);

        await _database.History.AddAsync([Event(LibraryEventKind.Downloaded, "3", Now)], Now.AddDays(-300), Cancellation);

        Assert.Equal(["3", "2"], (await _database.History.ListAsync(null, 10, Cancellation)).Select(happened => happened.File!.Id));
        Assert.Equal(2L, await _database.QueryAsync("SELECT COUNT(*) FROM LibraryEvents"));
    }

    private static LibraryEvent Event(LibraryEventKind kind, string fileId, DateTimeOffset at)
        => new(at, kind, SkyUi, "SkyUI", new ModFileRef(SkyUi, fileId), $"SkyUI {fileId}", RelativePath: $"SkyUI {fileId}.7z");
}
