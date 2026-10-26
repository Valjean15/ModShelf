using System.Net;

using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Tests.Updates;

public sealed class ModUpdatesTests
{
    private static readonly GameRef Skyrim = TestData.File(TestData.Nexus, "1").Mod.Game;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsksTheFeedOfTheGamesSource()
    {
        FakeUpdateFeed nexus = new(TestData.Nexus, Update("12604", hoursAgo: 1));
        FakeUpdateFeed modIo = new(TestData.ModIo);
        DateTimeOffset since = TestData.Now.AddDays(-1);

        IReadOnlyList<ModUpdate> updates = await new ModUpdates(new SourceRegistry([modIo, nexus])).GetUpdatesAsync(Skyrim, since, Cancellation);

        Assert.Equal("12604", Assert.Single(updates).Mod.Id);
        Assert.Equal((Skyrim, since), nexus.LastAsked);
        Assert.Null(modIo.LastAsked);
    }

    [Fact]
    public async Task PutsTheMostRecentlyActiveFirst()
    {
        FakeUpdateFeed nexus = new(
            TestData.Nexus,
            Update("3", hoursAgo: 5),
            Update("20", hoursAgo: 1),
            Update("10", hoursAgo: 5),
            Update("1", hoursAgo: 9));

        IReadOnlyList<ModUpdate> updates = await new ModUpdates(new SourceRegistry([nexus])).GetUpdatesAsync(Skyrim, TestData.Now.AddDays(-1), Cancellation);

        // Mods active at the same moment stay in the same order whatever the site's.
        Assert.Equal(["20", "10", "3", "1"], updates.Select(update => update.Mod.Id));
    }

    [Fact]
    public async Task RefusesASourceWithoutAFeed()
    {
        ModUpdates updates = new(new SourceRegistry([new CatalogOnlySource(TestData.Nexus)]));

        await Assert.ThrowsAsync<ArgumentException>(() => updates.GetUpdatesAsync(Skyrim, TestData.Now, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => updates.GetUpdatesAsync(TestData.File(TestData.ModIo, "1").Mod.Game, TestData.Now, Cancellation));
    }

    public static TheoryData<Exception> Failures() =>
    [
        new ArgumentOutOfRangeException("since", "The site lists the mods updated in the last 28 days only."),
        new ArgumentException("The site has no such game."),
        new AuthenticationFailedException("The site refused the account."),
        new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable),
    ];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task PassesOnWhatTheFeedRefuses(Exception failure)
    {
        FakeUpdateFeed nexus = new(TestData.Nexus) { Failure = failure };

        Exception thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => new ModUpdates(new SourceRegistry([nexus])).GetUpdatesAsync(Skyrim, TestData.Now, Cancellation));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void ListsTheSourcesWithAFeed()
    {
        ModUpdates updates = new(new SourceRegistry(
            [
                new FakeUpdateFeed(TestData.Nexus),
                new CatalogOnlySource(new SourceId("catalog-only")),
                new FakeUpdateFeed(TestData.ModIo),
            ]));

        Assert.Equal([TestData.Nexus, TestData.ModIo], updates.Sources);
    }

    private static ModUpdate Update(string mod, int hoursAgo)
        => new(new ModRef(Skyrim, mod), TestData.Now.AddHours(-hoursAgo));
}
