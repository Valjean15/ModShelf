using ModShelf.Application.Library;
using ModShelf.Application.Tests.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Library;

public sealed class LibraryChangesTests
{
    private static readonly ModRef SkyUi = TestData.File(TestData.Nexus, "35407").Mod;

    private readonly LibraryChanges _changes = new();

    [Fact]
    public async Task EveryWatcherGetsEveryChangeInOrder()
    {
        LibraryMod watched = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now);
        LibraryChange added = new(SkyUi, null, watched);
        LibraryChange removed = new(SkyUi, watched, null);
        using WatchedChanges first = new(_changes);
        using WatchedChanges second = new(_changes);

        _changes.Publish(added);
        _changes.Publish(removed);

        Assert.Equal([added, removed], await first.StopAsync());
        Assert.Equal([added, removed], await second.StopAsync());
    }

    [Fact]
    public async Task AChangeThatLeftTheModAsItWasIsntTold()
    {
        LibraryMod watched = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now);
        using WatchedChanges seen = new(_changes);

        _changes.Publish(new LibraryChange(SkyUi, watched, watched));
        _changes.Publish(new LibraryChange(SkyUi, null, null));

        Assert.Empty(await seen.StopAsync());
    }

    [Fact]
    public async Task AWatcherThatStoppedGetsNothingMore()
    {
        using WatchedChanges stopped = new(_changes);
        await stopped.StopAsync();
        using WatchedChanges still = new(_changes);

        LibraryChange added = new(SkyUi, null, LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now));
        _changes.Publish(added);

        Assert.Equal([added], await still.StopAsync());
    }
}
