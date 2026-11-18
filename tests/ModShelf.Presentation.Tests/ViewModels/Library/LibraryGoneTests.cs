using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Mods gone from their sites in the Library: each says so first, since when, and the toggle that shows them alone is
/// offered only while one is gone. The time zone is UTC, and today is October 27, 2026.
/// </summary>
public sealed class LibraryGoneTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("32444");

    private readonly LibraryHarness _harness = new();
    private readonly CultureScope _culture = new("en-US");

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose()
    {
        _culture.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public async Task AGoneModSaysSoFirstWithTheDayItLeft()
    {
        LibraryMod gone = LibraryMod.Watch(SkyUi, "SkyUI", null, null, TestData.Now.AddDays(-30)).LeftSite(TestData.Now.AddDays(-3)).WithPinned(true);
        await _harness.StartAsync(gone);

        LibraryModViewModel row = _harness.Row(gone);

        Assert.True(row.IsGone);
        Assert.Equal(
            ["Gone from Nexus Mods (Problem)", "Pinned (Note)", "Watched only (Note)"],
            row.Statuses.Select(status => $"{status.Text} ({status.Kind})"));
        Assert.Equal("Gone from Nexus Mods since 10/24/2026: your copies of its files may be the only ones left.", row.Statuses[0].Detail);
        Assert.Null(row.Statuses[1].Detail);
    }

    [Fact]
    public async Task TheGoneToggleShowsOnlyWhileAModIsGoneAndNarrowsToThem()
    {
        LibraryMod skyUi = TestData.LibraryMod(SkyUi, "SkyUI");
        await _harness.StartAsync(skyUi, TestData.LibraryMod(Address, "Address Library"));
        _ = _harness.Row(skyUi);
        ModFilterViewModel toggle = Screen.ModFilters.Single(filter => filter.Kind == ModFilterKind.Gone);
        (bool, int) none = (toggle.IsShown, toggle.Count);

        _harness.Library.Change(SkyUi, skyUi.LeftSite(TestData.Now));
        await Screen.Checking;
        (bool, int) one = (toggle.IsShown, toggle.Count);
        toggle.IsOn = true;
        string[] shown = [.. Screen.GameMods.Select(mod => mod.Name)];
        _harness.Library.Change(SkyUi, skyUi);
        await Screen.Checking;

        Assert.Equal((false, 0), none);
        Assert.Equal((true, 1), one);
        Assert.Equal(["SkyUI"], shown);
        Assert.Equal((true, 0), (toggle.IsShown, toggle.Count));
        Assert.Equal("Gone from their site", toggle.Label);
        Assert.All(Screen.ModFilters.Where(filter => !filter.HidesWhenNone), filter => Assert.True(filter.IsShown));
    }
}
