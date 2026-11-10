using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Sources.Abstractions.Tests.Search;

public sealed class SearchPageTests
{
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");

    [Theory]
    [InlineData(0, 2, 2, false)]
    [InlineData(0, 2, 3, true)]
    [InlineData(20, 2, 22, false)]
    [InlineData(20, 2, 30, true)]
    [InlineData(40, 0, 30, false)]
    public void SaysWhetherMoreModsMatch(int offset, int onPage, int total, bool more)
    {
        SearchPage page = new(Mods(onPage), total, offset);

        Assert.Equal(more, page.HasMore);
    }

    [Theory]
    [InlineData(0, 18, 2, 20, 30, true)]
    [InlineData(20, 8, 2, 30, 30, false)]
    [InlineData(20, 0, 10, 30, 30, false)]
    [InlineData(0, 0, 20, 20, 30, true)]
    public void CountsTheModsLeftOutAsOnThePage(int offset, int onPage, int leftOut, int next, int total, bool more)
    {
        SearchPage page = new(Mods(onPage), total, offset, leftOut);

        Assert.Equal((leftOut, next, more), (page.LeftOut, page.NextOffset, page.HasMore));
    }

    [Fact]
    public void LeavesNothingOutUnlessTold()
    {
        SearchPage page = new(Mods(2), 5, 0);

        Assert.Equal((0, 2), (page.LeftOut, page.NextOffset));
    }

    [Fact]
    public void KeepsItsOwnCopyOfTheMods()
    {
        List<ModInfo> mods = [.. Mods(1)];
        SearchPage page = new(mods, 1, 0);

        mods.Clear();

        Assert.Single(page.Mods);
    }

    [Fact]
    public void RefusesANullMod()
    {
        Assert.Throws<ArgumentException>(() => new SearchPage([null!], 1, 0));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void RefusesNegativeCounts(int total, int offset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPage([], total, offset));
    }

    [Fact]
    public void RefusesANegativeCountLeftOut()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPage([], 1, 0, leftOut: -1));
    }

    private static IEnumerable<ModInfo> Mods(int count)
        => Enumerable.Range(1, count).Select(id => new ModInfo(new ModRef(Skyrim, id.ToString(System.Globalization.CultureInfo.InvariantCulture)), $"Mod {id}"));
}
