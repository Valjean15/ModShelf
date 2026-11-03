using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// The library's history itself: what it lists, and keeping what the library's services tell it, a year of it.
/// </summary>
public sealed class LibraryHistoryTests
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryLibraryHistory _repository = new();
    private readonly RecordingLogger<LibraryHistory> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsWhatWasKeptTheNewestFirst()
    {
        LibraryHistory history = History();
        await history.RecordAsync([Downloaded("1", TestData.Now.AddDays(-2)), Downloaded("2", TestData.Now.AddDays(-1))], Cancellation);
        await history.RecordAsync([Downloaded("3", TestData.Now)], Cancellation);

        Assert.Equal(["3", "2", "1"], (await history.ListAsync(null, 10, Cancellation)).Select(happened => happened.File!.Id));
        Assert.Equal(["3", "2"], (await history.ListAsync(TestData.Now.AddDays(-1), 10, Cancellation)).Select(happened => happened.File!.Id));
        Assert.Equal(["3"], (await history.ListAsync(null, 1, Cancellation)).Select(happened => happened.File!.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task ListsFromOneToAThousand(int limit)
    {
        LibraryHistory history = History();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => history.ListAsync(null, limit, Cancellation));
        Assert.Empty(await history.ListAsync(null, 1, Cancellation));
        Assert.Empty(await history.ListAsync(null, LibraryHistory.MaxLimit, Cancellation));
    }

    [Fact]
    public async Task KeepsAYearAndForgetsWhatsOlder()
    {
        LibraryHistory history = History();
        await history.RecordAsync([Downloaded("1", TestData.Now.AddDays(-400)), Downloaded("2", TestData.Now.AddDays(-300))], Cancellation);

        _time.Advance(TimeSpan.FromDays(100));
        await history.RecordAsync([Downloaded("3", _time.GetUtcNow())], Cancellation);

        Assert.Equal(TestData.Now.AddDays(100 - 365), _repository.KeptSince);
        Assert.Equal(["3"], _repository.Events.Select(happened => happened.File!.Id));
    }

    [Fact]
    public async Task NothingToKeepChangesNothing()
    {
        await History().RecordAsync([], Cancellation);

        Assert.Null(_repository.KeptSince);
    }

    [Fact]
    public async Task WhatCantBeKeptIsLoggedAndTheLibraryIsntTold()
    {
        InvalidDataException failure = new("The database is locked.");
        _repository.Failure = failure;

        await History().RecordAsync([Downloaded("1", TestData.Now), Downloaded("2", TestData.Now)], Cancellation);

        Assert.StartsWith(
            "Warning: Couldn't keep 2 of the library's events in its history System.IO.InvalidDataException: The database is locked.",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepingWhatsCancelledIsCancelled()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        _repository.Failure = new OperationCanceledException(cancelled.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => History().RecordAsync([Downloaded("1", TestData.Now)], cancelled.Token));
        Assert.Empty(_logger.Entries);
    }

    private static LibraryEvent Downloaded(string fileId, DateTimeOffset at)
        => new(at, LibraryEventKind.Downloaded, Mod("12604"), "SkyUI", new ModFileRef(Mod("12604"), fileId), $"SkyUI {fileId}");

    private LibraryHistory History() => new(_repository, _time, _logger);
}
