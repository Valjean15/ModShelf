using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Tests.Fakes;

namespace ModShelf.Presentation.Tests.Common;

public sealed class LibraryFeedTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsTheLibraryThenFollowsItsChanges()
    {
        InMemoryModLibrary library = new();
        library.Add(TestData.LibraryMod(TestData.Mod("1"), "SkyUI"));
        List<IReadOnlyList<LibraryMod>> listed = [];
        List<LibraryChange> changed = [];
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        Task following = LibraryFeed.FollowAsync(library, new QueuedUiThread(), listed.Add, changed.Add, stopping.Token);
        await library.WaitedOn.WaitAsync(Patience, Cancellation);
        LibraryChange change = library.Change(TestData.Mod("2"), TestData.LibraryMod(TestData.Mod("2"), "SMAPI"));
        await stopping.CancelAsync();
        await following.WaitAsync(Patience, Cancellation);

        Assert.Equal(["SkyUI"], Assert.Single(listed).Select(mod => mod.Name));
        Assert.Equal([change], changed);
    }

    [Fact]
    public async Task AChangeMadeWhileTheLibraryIsListedIsntMissed()
    {
        InMemoryModLibrary library = new() { ListingHeld = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        List<LibraryChange> changed = [];
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        Task following = LibraryFeed.FollowAsync(library, new QueuedUiThread(), _ => { }, changed.Add, stopping.Token);

        // Told to every watcher, waiting until each has handled it: so off the test's thread.
        Task<LibraryChange> changing = Task.Run(() => library.Change(TestData.Mod("2"), TestData.LibraryMod(TestData.Mod("2"), "SMAPI")), Cancellation);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        library.ListingHeld.SetResult();
        LibraryChange change = await changing.WaitAsync(Patience, Cancellation);
        await stopping.CancelAsync();
        await following.WaitAsync(Patience, Cancellation);

        Assert.Equal([change], changed);
    }

    [Fact]
    public void KeepsEachModAsAChangeLeftIt()
    {
        ModRef skyUi = TestData.Mod("1");
        ModRef smapi = TestData.Mod("2");
        Dictionary<ModRef, LibraryMod> mods = [];
        mods.Keep([TestData.LibraryMod(skyUi, "SkyUI"), TestData.LibraryMod(smapi, "SMAPI")]);

        mods.Apply(new LibraryChange(skyUi, mods[skyUi], TestData.LibraryMod(skyUi, "SkyUI SE")));
        mods.Apply(new LibraryChange(smapi, mods[smapi], After: null));

        Assert.Equal(["SkyUI SE"], mods.Values.Select(mod => mod.Name));
    }
}
