using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Sources.Abstractions.Tests.Search;

public sealed class SearchQueryTests
{
    [Fact]
    public void LooksAtTheFirstPageOfEveryModByRelevanceWithoutAdultContent()
    {
        SearchQuery query = new();

        Assert.Equal(
            ((string?)null, SearchSort.Relevance, 0, 20, (string?)null, false),
            (query.Text, query.Sort, query.Offset, query.Count, query.Author, query.IncludeAdultContent));
        Assert.Equal(SearchDirection.Descending, query.Direction);
    }

    [Fact]
    public void KeepsWhatWasAsked()
    {
        SearchQuery query = new(" SkyUI ", SearchSort.Downloads, 40, 100, " schlangster ", includeAdultContent: true, SearchDirection.Ascending);

        Assert.Equal(
            ("SkyUI", SearchSort.Downloads, 40, 100, "schlangster", true),
            (query.Text, query.Sort, query.Offset, query.Count, query.Author, query.IncludeAdultContent));
        Assert.Equal(SearchDirection.Ascending, query.Direction);
    }

    [Fact]
    public void TreatsBlankTextAndAuthorAsNone()
    {
        SearchQuery query = new(" ", author: "");

        Assert.Null(query.Text);
        Assert.Null(query.Author);
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, SearchQuery.MaxCount + 1)]
    public void RefusesAPageOutOfRange(int offset, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(offset: offset, count: count));
    }

    [Fact]
    public void RefusesAnUnknownSort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(sort: (SearchSort)99));
    }

    [Fact]
    public void RefusesAnUnknownDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchQuery(direction: (SearchDirection)2));
    }
}
