using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Sources.Abstractions.Tests.Search;

public sealed class SearchCapabilitiesTests
{
    private static readonly SearchOrder BestMatch = new(SearchSort.Relevance, SearchDirection.Descending);
    private static readonly SearchOrder MostDownloaded = new(SearchSort.Downloads, SearchDirection.Descending);
    private static readonly SearchOrder LeastDownloaded = new(SearchSort.Downloads, SearchDirection.Ascending);

    [Fact]
    public void KeepsItsOrdersInTheirOrderOnceEachItsFiltersAndItsLargestPage()
    {
        SearchCapabilities can = new([BestMatch, MostDownloaded, BestMatch, LeastDownloaded], SearchFilters.Text | SearchFilters.Author, 50);

        Assert.Equal([BestMatch, MostDownloaded, LeastDownloaded], can.Orders);
        Assert.Equal((SearchFilters.Text | SearchFilters.Author, 50), (can.Filters, can.MaxCount));
    }

    [Fact]
    public void SaysWhichOrdersItHasEitherWayRound()
    {
        SearchCapabilities can = new([BestMatch, MostDownloaded], SearchFilters.None, 20);

        Assert.True(can.Has(MostDownloaded));
        Assert.False(can.Has(LeastDownloaded));
        Assert.True(can.Has(SearchSort.Downloads));
        Assert.False(can.Has(SearchSort.Name));
    }

    [Fact]
    public void SaysWhetherItHasEveryFilterAsked()
    {
        SearchCapabilities can = new([BestMatch], SearchFilters.Text | SearchFilters.UpdatedSince, 20);

        Assert.True(can.Has(SearchFilters.Text));
        Assert.True(can.Has(SearchFilters.Text | SearchFilters.UpdatedSince));
        Assert.False(can.Has(SearchFilters.Text | SearchFilters.Author));
        Assert.False(can.Has(SearchFilters.AdultContent));
        Assert.True(can.Has(SearchFilters.None));
    }

    [Fact]
    public void RefusesNoOrder()
    {
        Assert.Throws<ArgumentException>(() => new SearchCapabilities([], SearchFilters.Text, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SearchQuery.MaxCount + 1)]
    public void RefusesAPageSizeOutOfRange(int maxCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchCapabilities([BestMatch], SearchFilters.None, maxCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SearchQuery.MaxCount)]
    public void TakesAPageSizeInRange(int maxCount)
    {
        Assert.Equal(maxCount, new SearchCapabilities([BestMatch], SearchFilters.None, maxCount).MaxCount);
    }

    [Fact]
    public void RefusesAnUnknownFilter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchCapabilities([BestMatch], (SearchFilters)32, 20));
    }

    [Fact]
    public void IsTheSameAsAnotherSayingTheSame()
    {
        SearchCapabilities one = new([BestMatch, MostDownloaded], SearchFilters.Text, 50);
        SearchCapabilities same = new([BestMatch, MostDownloaded], SearchFilters.Text, 50);

        Assert.Equal(one, same);
        Assert.Equal(one.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(one, new SearchCapabilities([MostDownloaded, BestMatch], SearchFilters.Text, 50));
        Assert.NotEqual(one, new SearchCapabilities([BestMatch, MostDownloaded], SearchFilters.Author, 50));
        Assert.NotEqual(one, new SearchCapabilities([BestMatch, MostDownloaded], SearchFilters.Text, 20));
    }

    [Fact]
    public void AnOrderGoesEitherWayRound()
    {
        Assert.Equal([MostDownloaded, LeastDownloaded], SearchOrder.BothWays(SearchSort.Downloads));
    }

    [Fact]
    public void AnOrderRefusesAnUnknownSortOrDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchOrder((SearchSort)99, SearchDirection.Descending));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchOrder(SearchSort.Name, (SearchDirection)2));
    }
}
